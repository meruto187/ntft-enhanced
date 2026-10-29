using System.Net;
using System.Net.Sockets;
using System.Text;
using NtftEnhanced.Hosts;
using NtftEnhanced.Lists;
using NtftEnhanced.Proxy;
using NtftEnhanced.Security;

namespace NtftEnhanced.Tests;

public class HostsTests
{
    [Fact]
    public void Empty_git_blob_matches_git()
    {
        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", GitBlob.Sha1Hex([]));
    }

    [Fact]
    public void Parser_rejects_unexpected_address_and_keeps_header()
    {
        var bad = HostsListParser.Parse("0.0.0.0 blocked.example\n1.2.3.4 google.com\n");
        Assert.False(bad.IsValid);

        var good = HostsListParser.Parse("""
            # comment
            127.0.0.1 localhost
            ::1 localhost
            0.0.0.0 0.0.0.0
            127.0.0.1 blocked.example
            0.0.0.0 second.example
            """);
        Assert.True(good.IsValid);
        Assert.Equal(["blocked.example", "second.example"], good.Domains);
    }

    [Fact]
    public void Merge_keeps_user_lines_and_skips_canaries()
    {
        var existing = "127.0.0.1 localhost\r\n::1 localhost\r\n1.2.3.4 mine.local\r\n";
        var merged = HostsMerger.Merge(existing, ["blocked.example", "microsoft.com", "allowed.example"], new HashSet<string>(["allowed.example"]));
        Assert.True(merged.Success);
        Assert.Contains("1.2.3.4 mine.local", merged.Text);
        Assert.Contains("127.0.0.1 localhost", merged.Text);
        Assert.Contains("0.0.0.0 blocked.example", merged.Text);
        Assert.Contains(":: blocked.example", merged.Text);
        Assert.DoesNotContain("microsoft.com", merged.Text);
        Assert.DoesNotContain("allowed.example", merged.Text);

        var again = HostsMerger.Merge(merged.Text, ["fresh.example"], new HashSet<string>());
        Assert.Contains("fresh.example", again.Text);
        Assert.DoesNotContain("blocked.example", again.Text);
        Assert.Contains("mine.local", again.Text);
    }

    [Fact]
    public void Apply_rolls_back_a_half_write_and_remove_keeps_original_lines()
    {
        using var sandbox = new Sandbox();
        var store = new HostsFileStore(sandbox.Paths.HostsPath, sandbox.Paths.BackupDirectory);
        var original = store.Read();
        var merged = HostsMerger.Merge(original, ["blocked.example"], new HashSet<string>());
        store.FailBeforeReplace = () => true;
        var error = store.Apply(merged.Text);
        Assert.NotNull(error);
        Assert.Equal(original, store.Read());
        Assert.False(File.Exists(sandbox.Paths.HostsPath + ".ntft-tmp"));

        store.FailBeforeReplace = null;
        Assert.Null(store.Apply(merged.Text));
        Assert.True(store.ContainsManagedBlock());
        Assert.Null(store.RemoveManagedBlock());
        var restored = store.Read();
        Assert.Contains("mine.local", restored);
        Assert.Contains("127.0.0.1 localhost", restored);
        Assert.DoesNotContain(Product.HostsBegin, restored);
        Assert.True(File.Exists(Path.Combine(sandbox.Paths.BackupDirectory, "hosts-original")));
    }

    [Fact]
    public void Verifier_rejects_a_sha_mismatch_and_a_short_list()
    {
        var bytes = Encoding.UTF8.GetBytes("0.0.0.0 blocked.example\n");
        var wrong = ListVerifier.Accept(bytes, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1);
        Assert.False(wrong.Accepted);
        var shortList = ListVerifier.Accept(bytes, GitBlob.Sha1Hex(bytes), 10);
        Assert.False(shortList.Accepted);
        var ok = ListVerifier.Accept(bytes, GitBlob.Sha1Hex(bytes), 1);
        Assert.Equal("blocked.example", Assert.Single(ok.Domains));
    }
}

public class PasswordTests
{
    [Fact]
    public void Production_iteration_count_is_the_chosen_cost()
    {
        Assert.Equal(600_000, PasswordHasher.ProductionIterations);
    }

    [Fact]
    public void Hash_verifies_and_rate_limit_survives_a_new_instance()
    {
        var record = PasswordHasher.Hash("correct-horse", 1_000);
        Assert.True(PasswordHasher.Verify("correct-horse", record));
        Assert.False(PasswordHasher.Verify("wrong-horse", record));

        var clock = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "attempts.json");
        var limiter = new AttemptLimiter(path);
        for (var i = 0; i < 4; i++)
            Assert.Null(limiter.RegisterFailure(clock));
        var lockout = limiter.RegisterFailure(clock);
        Assert.Equal(TimeSpan.FromSeconds(60), lockout);

        var restarted = new AttemptLimiter(path);
        Assert.Equal(TimeSpan.FromSeconds(60), restarted.Remaining(clock.AddSeconds(10)));
        clock = clock.AddSeconds(61);
        Assert.Null(restarted.Remaining(clock));
        for (var i = 0; i < 4; i++)
            restarted.RegisterFailure(clock);
        Assert.Equal(TimeSpan.FromSeconds(120), restarted.RegisterFailure(clock));
        restarted.RegisterSuccess();
        Assert.Null(new AttemptLimiter(path).Remaining(clock));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Recovery_key_normalizes_dashes()
    {
        var (display, record) = RecoveryKey.Create(1_000);
        Assert.True(PasswordHasher.Verify(RecoveryKey.Normalize(display.ToLowerInvariant()), record));
        Assert.Equal(32, RecoveryKey.Normalize(display).Length);
    }
}

public class ProxyTests
{
    [Fact]
    public void Pac_falls_through_when_the_proxy_port_is_absent()
    {
        Assert.Contains("DIRECT", PacFile.Create(null));
        Assert.DoesNotContain("PROXY", PacFile.Create(null));
        var live = PacFile.Create(8873);
        Assert.Contains("PROXY 127.0.0.1:8873; DIRECT", live);
    }

    [Fact]
    public void Connect_parser_reads_the_host()
    {
        Assert.True(ConnectRequest.TryGetHost("CONNECT Blocked.Example:443 HTTP/1.1\r\nHost: Blocked.Example:443\r\n\r\n", out var host));
        Assert.Equal("blocked.example", host);
        Assert.True(ConnectRequest.TryGetHost("GET http://second.example/path HTTP/1.1\r\n\r\n", out host));
        Assert.Equal("second.example", host);
    }

    [Fact]
    public async Task Proxy_refuses_a_listed_host_without_writing_a_page()
    {
        var proxy = new LocalProxy();
        string? seen = null;
        await proxy.StartAsync(host =>
        {
            seen = host;
            return host == "blocked.example";
        }, () => { });
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes("CONNECT blocked.example:443 HTTP/1.1\r\nHost: blocked.example:443\r\n\r\n");
            await stream.WriteAsync(request);
            var buffer = new byte[512];
            var read = await stream.ReadAsync(buffer);
            var response = Encoding.ASCII.GetString(buffer, 0, read);
            Assert.Contains("403", response);
            Assert.Contains("Content-Length: 0", response);
            Assert.DoesNotContain("Engellendi", response);
            Assert.Equal("blocked.example", seen);
        }
        finally
        {
            await proxy.StopAsync();
        }
    }

    [Fact]
    public async Task Proxy_relays_an_allowed_connection_unchanged()
    {
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        var echoTask = Task.Run(async () =>
        {
            using var remote = await echo.AcceptTcpClientAsync();
            using var remoteStream = remote.GetStream();
            var buffer = new byte[4];
            var got = await remoteStream.ReadAsync(buffer);
            await remoteStream.WriteAsync(buffer.AsMemory(0, got));
        });

        var proxy = new LocalProxy();
        await proxy.StartAsync(_ => false, () => { });
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            using var stream = client.GetStream();
            var header = Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{echoPort} HTTP/1.1\r\nHost: 127.0.0.1:{echoPort}\r\n\r\n");
            await stream.WriteAsync(header);
            var response = new byte[128];
            var read = await stream.ReadAsync(response);
            Assert.Contains("200", Encoding.ASCII.GetString(response, 0, read));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("ping"));
            var body = new byte[4];
            var got = 0;
            while (got < 4)
                got += await stream.ReadAsync(body.AsMemory(got));
            Assert.Equal("ping", Encoding.ASCII.GetString(body));
            await echoTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await proxy.StopAsync();
            echo.Stop();
        }
    }
}
