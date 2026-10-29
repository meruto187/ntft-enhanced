namespace NtftEnhanced.Protection;

public sealed record ProtectionPaths(string HostsPath, string DataDirectory)
{
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
    public string StatePath => Path.Combine(DataDirectory, "state.json");
    public string DomainsPath => Path.Combine(DataDirectory, "domains.txt");
    public string AttemptsPath => Path.Combine(DataDirectory, "attempts.json");
    public string LogPath => Path.Combine(DataDirectory, "logs", "ntft.log");
    public string PacPath => Path.Combine(DataDirectory, "proxy.pac");
    public string HardeningPath => Path.Combine(DataDirectory, "hardening.json");

    public static ProtectionPaths Production()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Product.DataDirectoryName);
        var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        return new ProtectionPaths(hosts, data);
    }
}

public sealed record ProtectionOptions(int MinimumDomains, int PasswordIterations, TimeSpan UpdateInterval)
{
    public static ProtectionOptions Production { get; } = new(Product.ProductionMinimumDomains, Security.PasswordHasher.ProductionIterations, TimeSpan.FromDays(7));
}

public sealed class FilterState
{
    public bool ProtectionEnabled { get; set; }
    public bool Locked { get; set; }
    public string? Password { get; set; }
    public string? Recovery { get; set; }
    public List<string> Allowlist { get; set; } = [];
    public bool AutoUpdate { get; set; } = true;
    public DateTimeOffset? LastListUpdateUtc { get; set; }
    public DateTimeOffset? LastBlockUtc { get; set; }
    public long BlockCount { get; set; }
    public string? ListSha { get; set; }
    public int DomainCount { get; set; }
    public bool DohDisabled { get; set; }
    public bool SafeSearch { get; set; }
    public bool FamilyDns { get; set; }
    public bool DohIpsBlocked { get; set; }
}

public sealed record StatusSnapshot(
    bool ProtectionEnabled,
    bool Locked,
    bool AutoUpdate,
    int DomainCount,
    DateTimeOffset? LastListUpdateUtc,
    DateTimeOffset? LastBlockUtc,
    long BlockCount,
    bool ProxyRunning,
    int ProxyPort,
    bool DohDisabled,
    bool SafeSearch,
    bool FamilyDns,
    bool DohIpsBlocked,
    bool QuicDisabled,
    string PacPath);

public sealed record OperationResult(bool Success, string Message, string? RecoveryKey = null)
{
    public static OperationResult Done(string message = "", string? recoveryKey = null) => new(true, message, recoveryKey);
    public static OperationResult Fail(string message) => new(false, message);
}
