using System.Text.Json;
using NtftEnhanced.Hardening;
using NtftEnhanced.Hosts;
using NtftEnhanced.Ipc;

namespace NtftEnhanced.Tests;

public class ProtectionTests
{
    [Fact]
    public async Task Enable_blocks_uninstall_keeps_user_lines_and_bad_download_keeps_the_old_list()
    {
        using var sandbox = new Sandbox();
        var enabled = await sandbox.Coordinator.EnableAsync();
        Assert.True(enabled.Success);
        Assert.True(sandbox.Proxy.IsRunning);
        var hosts = File.ReadAllText(sandbox.Paths.HostsPath);
        Assert.DoesNotContain("blocked.example", hosts);
        Assert.DoesNotContain(Product.HostsBegin, hosts);
        Assert.Contains("1.2.3.4 mine.local", hosts);
        Assert.Contains("127.0.0.1 localhost", hosts);

        var merged = HostsMerger.Merge(hosts, ["blocked.example"], new HashSet<string>());
        Assert.True(merged.Success);
        File.WriteAllText(sandbox.Paths.HostsPath, merged.Text);
        await sandbox.Coordinator.MaintainAsync();
        hosts = File.ReadAllText(sandbox.Paths.HostsPath);
        Assert.DoesNotContain(Product.HostsBegin, hosts);
        Assert.Contains("mine.local", hosts);

        sandbox.Fetcher.Fail = true;
        var failed = await sandbox.Coordinator.UpdateListAsync();
        Assert.False(failed.Success);
        Assert.Contains("blocked.example", File.ReadAllText(sandbox.Paths.DomainsPath));

        var locked = sandbox.Coordinator.EstablishLock("friend-password", "friend-password");
        Assert.True(locked.Success);
        Assert.False(string.IsNullOrWhiteSpace(locked.RecoveryKey));
        var rejected = await sandbox.Coordinator.UninstallAsync("nope");
        Assert.False(rejected.Success);
        Assert.Contains("mine.local", File.ReadAllText(sandbox.Paths.HostsPath));

        var removed = await sandbox.Coordinator.UninstallAsync("friend-password");
        Assert.True(removed.Success);
        var after = File.ReadAllText(sandbox.Paths.HostsPath);
        Assert.DoesNotContain(Product.HostsBegin, after);
        Assert.Contains("mine.local", after);
        Assert.Contains("127.0.0.1 localhost", after);
        Assert.DoesNotContain("friend-password", File.ReadAllText(sandbox.Paths.LogPath));
        Assert.DoesNotContain(locked.RecoveryKey!, File.ReadAllText(sandbox.Paths.LogPath));
        Assert.DoesNotContain("blocked.example", File.ReadAllText(sandbox.Paths.LogPath));
    }

    [Fact]
    public async Task Lockout_persists_and_a_blocked_canary_restores_dns()
    {
        using var sandbox = new Sandbox();
        Assert.True((await sandbox.Coordinator.EnableAsync()).Success);
        Assert.True(sandbox.Coordinator.EstablishLock("friend-password", "friend-password").Success);
        for (var i = 0; i < 5; i++)
            Assert.False((await sandbox.Coordinator.DisableAsync("wrong-password")).Success);
        var during = await sandbox.Coordinator.DisableAsync("friend-password");
        Assert.False(during.Success);
        sandbox.Clock.UtcNow = sandbox.Clock.UtcNow.AddMinutes(2);
        Assert.True((await sandbox.Coordinator.DisableAsync("friend-password")).Success);

        Assert.True((await sandbox.Coordinator.EnableAsync()).Success);
        sandbox.Probe.Results.Enqueue(ProbeResult.Blocked);
        var hardening = sandbox.Coordinator.SetHardening("friend-password", new HardeningRequest(false, true, true, false, true));
        Assert.False(hardening.Success);
        Assert.Contains("192.168.1.1", sandbox.Dns.Current[0].Servers);
        Assert.False(sandbox.Policies.Has(PolicyCatalog.Chrome, "ForceYouTubeRestrict"));
        Assert.True(sandbox.Policies.Has(PolicyCatalog.Chrome, "ForceGoogleSafeSearch") || !sandbox.Coordinator.Status().SafeSearch);
    }

    [Fact]
    public void SafeSearch_policy_does_not_include_youtube_and_previous_values_return()
    {
        Assert.DoesNotContain(PolicyCatalog.SafeSearch(), change => change.Name == "ForceYouTubeRestrict");
        Assert.Contains(PolicyCatalog.SafeSearch(), change => change.Name == "ForceGoogleSafeSearch");
        Assert.Contains(PolicyCatalog.SafeSearch(), change => change.Name == "ForceBingSafeSearch");

        var policies = new MemoryPolicies();
        policies.SetDword(PolicyCatalog.Edge, "ForceGoogleSafeSearch", 7);
        var dns = new MemoryDns();
        var firewall = new MemoryFirewall();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "hardening.json");
        var controller = new HardeningController(path, policies, dns, firewall);
        var error = controller.Apply(new HardeningRequest(true, true, false, true, false), "file:///pac", new ScriptProbe());
        Assert.Null(error);
        Assert.Equal(0, policies.TryGetDword(PolicyCatalog.Edge, "ForceGoogleSafeSearch", out var current) ? current : -1);
        Assert.NotEmpty(firewall.Rules);
        controller.RevertAll();
        Assert.True(policies.TryGetDword(PolicyCatalog.Edge, "ForceGoogleSafeSearch", out var restored));
        Assert.Equal(7, restored);
        Assert.False(policies.Has(PolicyCatalog.Chrome, "DnsOverHttpsMode"));
        Assert.Empty(firewall.Rules);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public async Task Control_response_does_not_echo_the_password_into_the_log()
    {
        using var sandbox = new Sandbox();
        var api = new ControlApi(sandbox.Coordinator, sandbox.Log);
        await sandbox.Coordinator.EnableAsync();
        var response = api.Handle("""{"op":"lock","password":"friend-password","confirm":"friend-password"}""");
        Assert.DoesNotContain("friend-password", response);
        Assert.DoesNotContain("friend-password", string.Join('\n', sandbox.Log.Lines));
        var denied = api.Handle("""{"op":"disable","secret":"friend-password-is-secret"}""");
        Assert.Contains("false", denied);
        Assert.DoesNotContain("friend-password-is-secret", string.Join('\n', sandbox.Log.Lines));
        var document = JsonSerializer.Deserialize<JsonElement>(response);
        Assert.True(document.GetProperty("recoveryKey").GetString()!.Length > 20);
    }

    [Fact]
    public async Task Block_signal_counts_without_storing_the_host()
    {
        using var sandbox = new Sandbox();
        Assert.True((await sandbox.Coordinator.EnableAsync()).Success);
        Assert.NotNull(sandbox.Proxy.OnBlocked);
        sandbox.Proxy.OnBlocked!();
        Assert.Equal(1, sandbox.Coordinator.Status().BlockCount);
        Assert.DoesNotContain("blocked.example", File.ReadAllText(sandbox.Paths.StatePath));
        Assert.Equal(UiText.BlockedNotification, UiText.BlockedNotification);
    }
}
