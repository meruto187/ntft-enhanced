using System.Text.Json;
using NtftEnhanced.Hardening;
using NtftEnhanced.Hosts;
using NtftEnhanced.Infrastructure;
using NtftEnhanced.Lists;
using NtftEnhanced.Proxy;
using NtftEnhanced.Security;

namespace NtftEnhanced.Protection;

public sealed class ProtectionCoordinator
{
    private readonly ProtectionPaths _paths;
    private readonly ProtectionOptions _options;
    private readonly IListFetcher _fetcher;
    private readonly IClock _clock;
    private readonly INameProbe _probe;
    private readonly IDnsFlusher _flusher;
    private readonly ILog _log;
    private readonly IProxyHost _proxy;
    private readonly HostsFileStore _hosts;
    private readonly AttemptLimiter _attempts;
    private readonly HardeningController _hardening;
    private readonly object _gate = new();
    private FilterState _state;
    private HashSet<string> _blocked = new(StringComparer.Ordinal);
    private HashSet<string> _allow = new(StringComparer.Ordinal);
    private DateTimeOffset _lastAutoAttempt = DateTimeOffset.MinValue;

    public ProtectionCoordinator(
        ProtectionPaths paths,
        ProtectionOptions options,
        IListFetcher fetcher,
        IClock clock,
        INameProbe probe,
        IDnsFlusher flusher,
        ILog log,
        IProxyHost proxy,
        IPolicyWriter policies,
        IDnsConfigurator dns,
        IFirewallConfigurator firewall)
    {
        _paths = paths;
        _options = options;
        _fetcher = fetcher;
        _clock = clock;
        _probe = probe;
        _flusher = flusher;
        _log = log;
        _proxy = proxy;
        Directory.CreateDirectory(paths.DataDirectory);
        _hosts = new HostsFileStore(paths.HostsPath, paths.BackupDirectory);
        _attempts = new AttemptLimiter(paths.AttemptsPath);
        _hardening = new HardeningController(paths.HardeningPath, policies, dns, firewall);
        _state = LoadState(paths.StatePath);
        LoadDomainSets(ReadDomains());
    }

    public StatusSnapshot Status()
    {
        lock (_gate)
        {
            return new StatusSnapshot(
                _state.ProtectionEnabled,
                _state.Locked,
                _state.AutoUpdate,
                _state.DomainCount,
                _state.LastListUpdateUtc,
                _state.LastBlockUtc,
                _state.BlockCount,
                _proxy.IsRunning,
                _proxy.Port,
                _state.DohDisabled,
                _state.SafeSearch,
                _state.FamilyDns,
                _state.DohIpsBlocked,
                _hardening.Snapshot.ProxyPolicies,
                _paths.PacPath);
        }
    }

    public async Task<OperationResult> EnableAsync()
    {
        if (ReadDomains().Count == 0)
        {
            var updated = await UpdateListAsync().ConfigureAwait(false);
            if (!updated.Success)
                return updated;
            if (ReadDomains().Count == 0)
                return OperationResult.Fail("Liste boş. Hosts dosyasına yazılmadı.");
        }

        var leftover = ClearHostsBlock();
        lock (_gate)
        {
            _state.ProtectionEnabled = true;
            SaveState();
        }
        try
        {
            await StartProxyAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            WritePac(null);
            lock (_gate)
            {
                _state.ProtectionEnabled = false;
                SaveState();
            }
            _log.Info("proxy-failed");
            return OperationResult.Fail("Vekil açılamadı. Hosts dosyasına liste yazılmadı.");
        }
        ApplyRememberedHardening();
        _log.Info("protection-on");
        return OperationResult.Done(leftover is null
            ? "Koruma açık. Liste vekilde durur, hosts dosyasına yazılmaz."
            : "Koruma açık. Liste vekilde durur. Eski hosts bloğu silinemedi; yedekten geri alın.");
    }

    public async Task<OperationResult> DisableAsync(string? secret)
    {
        var auth = Authorize(secret);
        if (!auth.Success)
            return auth;
        await _proxy.StopAsync().ConfigureAwait(false);
        WritePac(null);
        var leftover = ClearHostsBlock();
        _hardening.RevertAll();
        lock (_gate)
        {
            _state.ProtectionEnabled = false;
            SaveState();
        }
        _log.Info("protection-off");
        return OperationResult.Done(leftover is null
            ? "Koruma kapalı."
            : "Koruma kapalı. Eski hosts bloğu silinemedi; yedekten geri alın.");
    }

    public async Task<OperationResult> UninstallAsync(string? secret)
    {
        var disabled = await DisableAsync(secret).ConfigureAwait(false);
        if (!disabled.Success)
            return disabled;
        _log.Info("uninstall");
        return OperationResult.Done("Engelleme kaldırıldı. Yedekler duruyor.");
    }

    public async Task<OperationResult> UpdateListAsync()
    {
        ListFetchResult fetched;
        try
        {
            fetched = await _fetcher.FetchAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _log.Info("list-download-failed");
            return OperationResult.Fail("Liste indirilemedi. Eski liste duruyor.");
        }

        if (!fetched.Success || fetched.Bytes is null || fetched.Sha is null)
        {
            _log.Info("list-download-failed");
            return OperationResult.Fail(fetched.Error ?? "Liste indirilemedi. Eski liste duruyor.");
        }

        var verdict = ListVerifier.Accept(fetched.Bytes, fetched.Sha, _options.MinimumDomains);
        if (!verdict.Accepted)
        {
            _log.Info("list-rejected");
            return OperationResult.Fail(verdict.Error ?? "Liste doğrulanamadı. Eski liste duruyor.");
        }

        WriteDomains(verdict.Domains);
        lock (_gate)
        {
            LoadDomainSets(verdict.Domains);
            _state.DomainCount = verdict.Domains.Count;
            _state.ListSha = fetched.Sha;
            _state.LastListUpdateUtc = _clock.UtcNow;
            SaveState();
        }
        _log.Info("list-updated");
        return OperationResult.Done("Liste güncellendi.");
    }

    public OperationResult EstablishLock(string password, string confirm)
    {
        lock (_gate)
        {
            if (_state.Locked)
                return OperationResult.Fail("Kilit zaten kurulu.");
            if (password != confirm)
                return OperationResult.Fail("Parolalar aynı değil.");
            if (password.Length < 8)
                return OperationResult.Fail("Parola en az 8 karakter olsun.");
            var (display, recovery) = RecoveryKey.Create(_options.PasswordIterations);
            _state.Password = PasswordHasher.Hash(password, _options.PasswordIterations).ToStorage();
            _state.Recovery = recovery.ToStorage();
            _state.Locked = true;
            SaveState();
            _log.Info("lock-created");
            return OperationResult.Done("Kilit kuruldu. Kurtarma anahtarını makine dışında saklayın.", display);
        }
    }

    public OperationResult SetAllowlist(string? secret, IReadOnlyList<string> domains)
    {
        var auth = Authorize(secret);
        if (!auth.Success)
            return auth;
        var clean = new List<string>();
        foreach (var domain in domains)
        {
            var host = DomainName.Normalize(domain);
            if (host is null)
                return OperationResult.Fail("İzin listesi yalnızca tam alan adı kabul eder.");
            clean.Add(host);
        }
        lock (_gate)
        {
            _state.Allowlist = clean;
            LoadDomainSets(ReadDomains());
            SaveState();
        }
        _log.Info("allowlist");
        return OperationResult.Done("İzin listesi kaydedildi.");
    }

    public OperationResult SetAutoUpdate(string? secret, bool enabled)
    {
        var auth = Authorize(secret);
        if (!auth.Success)
            return auth;
        lock (_gate)
        {
            _state.AutoUpdate = enabled;
            SaveState();
        }
        _log.Info(enabled ? "autoupdate-on" : "autoupdate-off");
        return OperationResult.Done(enabled ? "Kendiliğinden güncelleme açık." : "Kendiliğinden güncelleme kapalı.");
    }

    public OperationResult SetHardening(string? secret, HardeningRequest request)
    {
        var auth = Authorize(secret);
        if (!auth.Success)
            return auth;
        lock (_gate)
        {
            _state.DohDisabled = request.Doh;
            _state.SafeSearch = request.SafeSearch;
            _state.FamilyDns = request.FamilyDns;
            _state.DohIpsBlocked = request.DohIps;
            SaveState();
        }
        if (Status().ProtectionEnabled)
        {
            var error = ApplyRememberedHardening();
            if (error is not null)
                return OperationResult.Fail(error);
        }
        _log.Info("hardening");
        return OperationResult.Done("Sertleştirme kaydedildi.");
    }

    public IReadOnlyList<string> Search(string query, int limit = 50)
    {
        var needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return [];
        return ReadDomains().Where(domain => domain.Contains(needle, StringComparison.Ordinal)).Take(limit).ToList();
    }

    public async Task MaintainAsync()
    {
        if (Status().ProtectionEnabled)
            ClearHostsBlock();

        if (Status().ProtectionEnabled && !_proxy.IsRunning)
        {
            try
            {
                await StartProxyAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                WritePac(null);
                _log.Info("proxy-failed");
            }
        }

        var status = Status();
        var due = status.LastListUpdateUtc is null || _clock.UtcNow - status.LastListUpdateUtc.Value >= _options.UpdateInterval;
        var waited = _clock.UtcNow - _lastAutoAttempt >= TimeSpan.FromHours(1);
        if (status.ProtectionEnabled && status.AutoUpdate && due && waited)
        {
            _lastAutoAttempt = _clock.UtcNow;
            await UpdateListAsync().ConfigureAwait(false);
        }
    }

    private async Task StartProxyAsync()
    {
        await _proxy.StartAsync(IsBlockedHost, NoteBlocked).ConfigureAwait(false);
        WritePac(_proxy.Port);
        _log.Info("proxy-on");
    }

    private string? ApplyRememberedHardening()
    {
        HardeningRequest request;
        lock (_gate)
        {
            request = new HardeningRequest(_state.DohDisabled, _state.SafeSearch, _state.FamilyDns, _state.DohIpsBlocked, true);
        }
        var pacUrl = "file:///" + _paths.PacPath.Replace('\\', '/');
        return _hardening.Apply(request, pacUrl, _probe);
    }

    private bool IsBlockedHost(string host)
    {
        lock (_gate)
        {
            if (_allow.Contains(host))
                return false;
            return _blocked.Contains(host);
        }
    }

    private void NoteBlocked()
    {
        lock (_gate)
        {
            _state.BlockCount++;
            _state.LastBlockUtc = _clock.UtcNow;
            SaveState();
        }
        _log.Info("blocked");
    }

    private string? ClearHostsBlock()
    {
        if (!_hosts.ContainsManagedBlock())
            return null;
        var error = _hosts.RemoveManagedBlock();
        if (error is null)
        {
            _flusher.Flush();
            _log.Info("hosts-block-removed");
            return null;
        }
        _log.Info("hosts-cleanup-failed");
        return error;
    }

    private OperationResult Authorize(string? secret)
    {
        lock (_gate)
        {
            if (!_state.Locked)
                return OperationResult.Done();
            var remaining = _attempts.Remaining(_clock.UtcNow);
            if (remaining is not null)
                return OperationResult.Fail($"Çok fazla deneme. {Math.Ceiling(remaining.Value.TotalSeconds)} saniye bekleyin.");
        }

        var passwordOk = false;
        var recoveryOk = false;
        lock (_gate)
        {
            passwordOk = PasswordHasher.VerifyStored(secret ?? "", _state.Password);
            if (!passwordOk && _state.Recovery is not null)
                recoveryOk = PasswordHasher.VerifyStored(RecoveryKey.Normalize(secret ?? ""), _state.Recovery);
        }

        if (!passwordOk && !recoveryOk)
        {
            _log.Info("auth-rejected");
            var lockout = _attempts.RegisterFailure(_clock.UtcNow);
            return lockout is null
                ? OperationResult.Fail("Parola kabul edilmedi.")
                : OperationResult.Fail($"Parola kabul edilmedi. {lockout.Value.TotalSeconds:0} saniye bekleyin.");
        }

        _attempts.RegisterSuccess();
        return OperationResult.Done();
    }

    private void WritePac(int? port)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.PacPath, PacFile.Create(port));
    }

    private void LoadDomainSets(IReadOnlyList<string> domains)
    {
        _blocked = domains.ToHashSet(StringComparer.Ordinal);
        _allow = _state.Allowlist.ToHashSet(StringComparer.Ordinal);
    }

    private List<string> ReadDomains()
    {
        if (!File.Exists(_paths.DomainsPath))
            return [];
        return File.ReadAllLines(_paths.DomainsPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private void WriteDomains(IReadOnlyList<string> domains)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllLines(_paths.DomainsPath, domains);
    }

    private void SaveState()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static FilterState LoadState(string path)
    {
        if (!File.Exists(path))
            return new FilterState();
        try
        {
            return JsonSerializer.Deserialize<FilterState>(File.ReadAllText(path)) ?? new FilterState();
        }
        catch (JsonException)
        {
            return new FilterState();
        }
    }
}
