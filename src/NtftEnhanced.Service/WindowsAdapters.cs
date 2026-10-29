using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using NtftEnhanced.Hardening;
using NtftEnhanced.Infrastructure;

namespace NtftEnhanced.ServiceHost;

public sealed class WindowsPolicyWriter : IPolicyWriter
{
    public bool TryGetString(string path, string name, out string? value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        value = key?.GetValue(name) as string;
        return value is not null;
    }

    public bool TryGetDword(string path, string name, out int value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key?.GetValue(name) is int number)
        {
            value = number;
            return true;
        }
        value = 0;
        return false;
    }

    public void SetString(string path, string name, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetDword(string path, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class WindowsDnsConfigurator : IDnsConfigurator
{
    public IReadOnlyList<DnsInterface> Read()
    {
        var json = Run("""
            Get-DnsClientServerAddress -AddressFamily IPv4,IPv6 |
              Where-Object { $_.ServerAddresses.Count -gt 0 } |
              Select-Object InterfaceIndex, AddressFamily, ServerAddresses |
              ConvertTo-Json -Compress
            """);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "")
            return [];
        using var document = System.Text.Json.JsonDocument.Parse(json.Trim());
        var root = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];
        var list = new List<DnsInterface>();
        foreach (var item in root)
        {
            var familyNode = item.GetProperty("AddressFamily");
            var family = familyNode.ValueKind == System.Text.Json.JsonValueKind.String
                ? (familyNode.GetString()?.Contains('6') == true ? "v6" : "v4")
                : (familyNode.GetInt32() == 23 ? "v6" : "v4");
            var servers = item.GetProperty("ServerAddresses").EnumerateArray().Select(value => value.GetString() ?? "").Where(value => value.Length > 0).ToArray();
            list.Add(new DnsInterface(item.GetProperty("InterfaceIndex").GetInt32(), family, servers));
        }
        return list;
    }

    public void Write(int index, string family, IReadOnlyList<string> servers)
    {
        var addressFamily = family == "v6" ? "IPv6" : "IPv4";
        var quoted = string.Join(",", servers.Select(server => "'" + server.Replace("'", "") + "'"));
        Run($"Set-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily {addressFamily} -ServerAddresses @({quoted})");
    }

    private static string Run(string command)
    {
        var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new IOException("PowerShell başlatılamadı.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(20000);
        return output;
    }
}

public sealed class WindowsFirewallConfigurator : IFirewallConfigurator
{
    public void EnsureBlock(string ruleName, string remoteIp)
    {
        Run($"advfirewall firewall delete rule name=\"{ruleName}\"");
        Run($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block protocol=TCP remoteport=443 remoteip={remoteIp}");
    }

    public void RemoveByPrefix(string prefix)
    {
        foreach (var ip in PolicyCatalog.DohIps)
            Run($"advfirewall firewall delete rule name=\"{prefix}{ip}\"");
    }

    private static void Run(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("netsh", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit(15000);
    }
}

public sealed class WindowsNameProbe : INameProbe
{
    public ProbeResult Probe(string host)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
                return ProbeResult.Unreachable;
            var blocked = addresses.All(address =>
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.IPv6None) ||
                address.Equals(IPAddress.Loopback) ||
                address.Equals(IPAddress.IPv6Loopback));
            return blocked ? ProbeResult.Blocked : ProbeResult.Ok;
        }
        catch (SocketException)
        {
            return ProbeResult.Unreachable;
        }
    }
}

public sealed class WindowsDnsFlush : IDnsFlusher
{
    public void Flush()
    {
        using var process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit(10000);
    }
}
