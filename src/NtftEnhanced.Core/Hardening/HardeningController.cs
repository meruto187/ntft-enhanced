using System.Text.Json;

namespace NtftEnhanced.Hardening;

public sealed class HardeningController
{
    private readonly string _statePath;
    private readonly IPolicyWriter _policies;
    private readonly IDnsConfigurator _dns;
    private readonly IFirewallConfigurator _firewall;
    private HardeningState _state;

    public HardeningController(string statePath, IPolicyWriter policies, IDnsConfigurator dns, IFirewallConfigurator firewall)
    {
        _statePath = statePath;
        _policies = policies;
        _dns = dns;
        _firewall = firewall;
        _state = Load(statePath);
    }

    public HardeningState Snapshot => _state;

    public string? Apply(HardeningRequest request, string pacUrl, Infrastructure.INameProbe probe)
    {
        SyncPolicies("doh", request.Doh, PolicyCatalog.DohOff());
        SyncPolicies("safe", request.SafeSearch, PolicyCatalog.SafeSearch());
        SyncPolicies("proxy", request.ProxyPolicies, PolicyCatalog.Proxy(pacUrl));
        var dnsError = SyncDns(request.FamilyDns, probe);
        var firewallError = SyncFirewall(request.DohIps, probe);
        _state.Doh = request.Doh;
        _state.SafeSearch = request.SafeSearch;
        _state.ProxyPolicies = request.ProxyPolicies;
        _state.FamilyDns = request.FamilyDns && dnsError is null;
        _state.DohIps = request.DohIps && firewallError is null;
        Save();
        return dnsError ?? firewallError;
    }

    public void RevertAll()
    {
        Apply(new HardeningRequest(false, false, false, false, false), "direct", new OkProbe());
    }

    private string? SyncDns(bool enabled, Infrastructure.INameProbe probe)
    {
        if (!enabled)
        {
            RestoreDns();
            return null;
        }
        if (_state.PreviousDns.Count == 0)
            _state.PreviousDns = _dns.Read().Select(item => new DnsInterface(item.Index, item.Family, item.Servers.ToArray())).ToList();
        if (_state.PreviousDns.Count == 0)
            return "DNS arayüzü bulunamadı. Değişiklik yapılmadı.";
        foreach (var item in _state.PreviousDns)
        {
            var servers = item.Family == "v6" ? PolicyCatalog.FamilyV6 : PolicyCatalog.FamilyV4;
            _dns.Write(item.Index, item.Family, servers);
        }
        if (probe.Probe("microsoft.com") == Infrastructure.ProbeResult.Blocked)
        {
            RestoreDns();
            return "DNS değişince kanarya çözülmedi. Eski DNS geri geldi.";
        }
        return null;
    }

    private string? SyncFirewall(bool enabled, Infrastructure.INameProbe probe)
    {
        if (!enabled)
        {
            _firewall.RemoveByPrefix(Product.FirewallPrefix);
            return null;
        }
        foreach (var ip in PolicyCatalog.DohIps)
            _firewall.EnsureBlock(Product.FirewallPrefix + ip, ip);
        if (probe.Probe("microsoft.com") == Infrastructure.ProbeResult.Blocked)
        {
            _firewall.RemoveByPrefix(Product.FirewallPrefix);
            return "Güvenlik duvarı kuralı kanaryayı düşürdü. Kurallar kaldırıldı.";
        }
        return null;
    }

    private void RestoreDns()
    {
        foreach (var item in _state.PreviousDns)
            _dns.Write(item.Index, item.Family, item.Servers);
        _state.PreviousDns.Clear();
    }

    private void SyncPolicies(string group, bool enabled, IReadOnlyList<PolicyChange> changes)
    {
        if (!enabled)
        {
            foreach (var saved in _state.Policies.Where(item => item.Group == group).ToList())
            {
                Restore(saved);
                _state.Policies.Remove(saved);
            }
            return;
        }

        foreach (var change in changes)
        {
            var existing = _state.Policies.FirstOrDefault(item => item.Group == group && item.Path == change.Path && item.Name == change.Name);
            if (existing is null)
            {
                _state.Policies.Add(Capture(group, change));
            }
            Write(change);
        }
    }

    private SavedPolicy Capture(string group, PolicyChange change)
    {
        var saved = new SavedPolicy { Group = group, Path = change.Path, Name = change.Name };
        if (change.Kind == "string" && _policies.TryGetString(change.Path, change.Name, out var text))
        {
            saved.Kind = "string";
            saved.StringValue = text;
        }
        else if (change.Kind == "dword" && _policies.TryGetDword(change.Path, change.Name, out var number))
        {
            saved.Kind = "dword";
            saved.DwordValue = number;
        }
        else
        {
            saved.Kind = "missing";
        }
        return saved;
    }

    private void Write(PolicyChange change)
    {
        if (change.Kind == "string")
            _policies.SetString(change.Path, change.Name, change.StringValue ?? "");
        else
            _policies.SetDword(change.Path, change.Name, change.DwordValue);
    }

    private void Restore(SavedPolicy saved)
    {
        if (saved.Kind == "missing")
            _policies.DeleteValue(saved.Path, saved.Name);
        else if (saved.Kind == "string")
            _policies.SetString(saved.Path, saved.Name, saved.StringValue ?? "");
        else
            _policies.SetDword(saved.Path, saved.Name, saved.DwordValue);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static HardeningState Load(string path)
    {
        if (!File.Exists(path))
            return new HardeningState();
        try
        {
            return JsonSerializer.Deserialize<HardeningState>(File.ReadAllText(path)) ?? new HardeningState();
        }
        catch (JsonException)
        {
            return new HardeningState();
        }
    }

    private sealed class OkProbe : Infrastructure.INameProbe
    {
        public Infrastructure.ProbeResult Probe(string host) => Infrastructure.ProbeResult.Ok;
    }
}
