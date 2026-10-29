namespace NtftEnhanced.Hardening;

public sealed record PolicyChange(string Path, string Name, string Kind, string? StringValue, int DwordValue);

public static class PolicyCatalog
{
    public const string Chrome = @"SOFTWARE\Policies\Google\Chrome";
    public const string Edge = @"SOFTWARE\Policies\Microsoft\Edge";
    public const string Brave = @"SOFTWARE\Policies\BraveSoftware\Brave";
    public const string Chromium = @"SOFTWARE\Policies\Chromium";
    public const string FirefoxDoh = @"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS";

    public static readonly string[] BrowserPaths = [Chrome, Edge, Brave, Chromium];
    public static readonly string[] DohIps = ["1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "149.112.112.112"];
    public static readonly string[] FamilyV4 = ["1.1.1.3", "1.0.0.3"];
    public static readonly string[] FamilyV6 = ["2606:4700:4700::1113", "2606:4700:4700::1003"];

    public static IReadOnlyList<PolicyChange> DohOff()
    {
        var list = new List<PolicyChange>();
        foreach (var path in BrowserPaths)
        {
            list.Add(new PolicyChange(path, "DnsOverHttpsMode", "string", "off", 0));
            list.Add(new PolicyChange(path, "BuiltInDnsClientEnabled", "dword", null, 0));
        }
        list.Add(new PolicyChange(FirefoxDoh, "Enabled", "dword", null, 0));
        list.Add(new PolicyChange(FirefoxDoh, "Locked", "dword", null, 1));
        return list;
    }

    public static IReadOnlyList<PolicyChange> SafeSearch()
    {
        var list = new List<PolicyChange>();
        foreach (var path in BrowserPaths)
            list.Add(new PolicyChange(path, "ForceGoogleSafeSearch", "dword", null, 1));
        list.Add(new PolicyChange(Edge, "ForceBingSafeSearch", "dword", null, 1));
        return list;
    }

    public static IReadOnlyList<PolicyChange> Proxy(string pacUrl)
    {
        var json = "{\"ProxyMode\":\"pac_script\",\"ProxyPacUrl\":\"" + pacUrl.Replace("\\", "\\\\").Replace("\"", "") + "\"}";
        var list = new List<PolicyChange>();
        foreach (var path in BrowserPaths)
        {
            list.Add(new PolicyChange(path, "QuicAllowed", "dword", null, 0));
            list.Add(new PolicyChange(path, "ProxySettings", "string", json, 0));
        }
        return list;
    }
}

public interface IPolicyWriter
{
    bool TryGetString(string path, string name, out string? value);
    bool TryGetDword(string path, string name, out int value);
    void SetString(string path, string name, string value);
    void SetDword(string path, string name, int value);
    void DeleteValue(string path, string name);
}

public sealed record DnsInterface(int Index, string Family, string[] Servers);

public interface IDnsConfigurator
{
    IReadOnlyList<DnsInterface> Read();
    void Write(int index, string family, IReadOnlyList<string> servers);
}

public interface IFirewallConfigurator
{
    void EnsureBlock(string ruleName, string remoteIp);
    void RemoveByPrefix(string prefix);
}

public sealed class SavedPolicy
{
    public string Group { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "missing";
    public string? StringValue { get; set; }
    public int DwordValue { get; set; }
}

public sealed class HardeningState
{
    public bool Doh { get; set; }
    public bool SafeSearch { get; set; }
    public bool FamilyDns { get; set; }
    public bool DohIps { get; set; }
    public bool ProxyPolicies { get; set; }
    public List<SavedPolicy> Policies { get; set; } = [];
    public List<DnsInterface> PreviousDns { get; set; } = [];
}

public sealed record HardeningRequest(bool Doh, bool SafeSearch, bool FamilyDns, bool DohIps, bool ProxyPolicies);
