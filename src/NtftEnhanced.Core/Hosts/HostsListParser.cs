namespace NtftEnhanced.Hosts;

public sealed record HostsParseResult(bool IsValid, IReadOnlyList<string> Domains, string? Error)
{
    public static HostsParseResult Ok(IReadOnlyList<string> domains) => new(true, domains, null);
    public static HostsParseResult Invalid(string error) => new(false, [], error);
}

public static class HostsListParser
{
    public static HostsParseResult Parse(string text)
    {
        var domains = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return HostsParseResult.Invalid($"Satır {lineNumber} eksik.");

            var ip = parts[0];
            var host = parts[1].Trim().TrimEnd('.').ToLowerInvariant();
            if (IsHeader(ip, host) || (ip == "0.0.0.0" && host == "0.0.0.0"))
                continue;
            if (ip is not ("0.0.0.0" or "127.0.0.1"))
                return HostsParseResult.Invalid($"Satır {lineNumber} beklenmeyen adres içeriyor.");
            if (!DomainName.IsValid(host) || DomainName.IsInfrastructure(host) || DomainName.IsCanary(host))
                continue;
            if (seen.Add(host))
                domains.Add(host);
        }

        return HostsParseResult.Ok(domains);
    }

    private static bool IsHeader(string ip, string host)
    {
        if ((ip is "127.0.0.1" or "::1") && DomainName.IsInfrastructure(host))
            return true;
        return ip == "255.255.255.255" && host == "broadcasthost";
    }
}
