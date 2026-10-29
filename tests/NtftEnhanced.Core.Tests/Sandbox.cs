using NtftEnhanced.Hardening;
using NtftEnhanced.Infrastructure;
using NtftEnhanced.Lists;
using NtftEnhanced.Protection;
using NtftEnhanced.Proxy;

namespace NtftEnhanced.Tests;

internal sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class ListLog : ILog
{
    public List<string> Lines { get; } = [];
    public void Info(string code) => Lines.Add(code);
}

internal sealed class ScriptProbe : INameProbe
{
    public Queue<ProbeResult> Results { get; } = new();
    public ProbeResult Probe(string host) => Results.Count == 0 ? ProbeResult.Ok : Results.Dequeue();
}

internal sealed class StaticFetcher : IListFetcher
{
    public byte[] Bytes { get; set; } = [];
    public string? ShaOverride { get; set; }
    public bool Fail { get; set; }

    public Task<ListFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        if (Fail)
            return Task.FromResult(ListFetchResult.Failed("indirilemedi"));
        var sha = ShaOverride ?? Hosts.GitBlob.Sha1Hex(Bytes);
        return Task.FromResult(ListFetchResult.Ready(Bytes, sha));
    }
}

internal sealed class RecordingProxy : IProxyHost
{
    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 8873;
    public Action? OnBlocked { get; private set; }
    public Func<string, bool>? Rule { get; private set; }

    public Task StartAsync(Func<string, bool> isBlocked, Action onBlocked)
    {
        Rule = isBlocked;
        OnBlocked = onBlocked;
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        return Task.CompletedTask;
    }
}

internal sealed class MemoryPolicies : IPolicyWriter
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetString(string path, string name, out string? value)
    {
        if (_values.TryGetValue(Key(path, name), out var stored) && stored is string text)
        {
            value = text;
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetDword(string path, string name, out int value)
    {
        if (_values.TryGetValue(Key(path, name), out var stored) && stored is int number)
        {
            value = number;
            return true;
        }
        value = 0;
        return false;
    }

    public void SetString(string path, string name, string value) => _values[Key(path, name)] = value;
    public void SetDword(string path, string name, int value) => _values[Key(path, name)] = value;
    public void DeleteValue(string path, string name) => _values.Remove(Key(path, name));
    public bool Has(string path, string name) => _values.ContainsKey(Key(path, name));
    private static string Key(string path, string name) => path + "|" + name;
}

internal sealed class MemoryDns : IDnsConfigurator
{
    public List<DnsInterface> Current { get; } = [new(1, "v4", ["192.168.1.1"])];
    public List<(int Index, string Family, string[] Servers)> Writes { get; } = [];

    public IReadOnlyList<DnsInterface> Read() => Current.Select(item => new DnsInterface(item.Index, item.Family, item.Servers.ToArray())).ToList();

    public void Write(int index, string family, IReadOnlyList<string> servers)
    {
        Writes.Add((index, family, servers.ToArray()));
        var existing = Current.FindIndex(item => item.Index == index && item.Family == family);
        var updated = new DnsInterface(index, family, servers.ToArray());
        if (existing >= 0)
            Current[existing] = updated;
        else
            Current.Add(updated);
    }
}

internal sealed class MemoryFirewall : IFirewallConfigurator
{
    public List<string> Rules { get; } = [];
    public void EnsureBlock(string ruleName, string remoteIp) => Rules.Add(ruleName);
    public void RemoveByPrefix(string prefix) => Rules.RemoveAll(rule => rule.StartsWith(prefix, StringComparison.Ordinal));
}

internal sealed class Sandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ntft-enhanced-tests", Guid.NewGuid().ToString("n"));
    public ProtectionPaths Paths { get; }
    public ManualClock Clock { get; } = new();
    public ListLog Log { get; } = new();
    public ScriptProbe Probe { get; } = new();
    public StaticFetcher Fetcher { get; } = new();
    public RecordingProxy Proxy { get; } = new();
    public MemoryPolicies Policies { get; } = new();
    public MemoryDns Dns { get; } = new();
    public MemoryFirewall Firewall { get; } = new();
    public ProtectionCoordinator Coordinator { get; }

    public Sandbox()
    {
        Directory.CreateDirectory(Root);
        var hosts = Path.Combine(Root, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n::1 localhost\r\n1.2.3.4 mine.local\r\n");
        Paths = new ProtectionPaths(hosts, Path.Combine(Root, "data"));
        Fetcher.Bytes = System.Text.Encoding.UTF8.GetBytes("""
            # test list
            127.0.0.1 localhost
            0.0.0.0 blocked.example
            0.0.0.0 second.example
            """);
        Coordinator = new ProtectionCoordinator(
            Paths,
            new ProtectionOptions(1, 1_000, TimeSpan.FromDays(7)),
            Fetcher,
            Clock,
            Probe,
            new NoDnsFlush(),
            Log,
            Proxy,
            Policies,
            Dns,
            Firewall);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}
