namespace NtftEnhanced.Hosts;

public static class DomainName
{
    public static bool IsInfrastructure(string host) => host is
        "localhost" or "localhost.localdomain" or "local" or "broadcasthost" or
        "ip6-localhost" or "ip6-loopback" or "ip6-allnodes" or "ip6-allrouters";

    public static bool IsCanary(string host)
    {
        foreach (var canary in Product.CanaryHosts)
        {
            if (string.Equals(canary, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsValid(string host)
    {
        if (host.Length is < 1 or > 253)
            return false;
        if (host.Contains('*') || host.Contains('/') || host.Contains(':') || host.Contains('@') || host.Contains(' '))
            return false;
        if (host.StartsWith('.') || host.EndsWith('.') || host.Contains("..", StringComparison.Ordinal))
            return false;

        var labels = host.Split('.');
        if (labels.Length < 2)
            return false;

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63)
                return false;
            if (label[0] == '-' || label[^1] == '-')
                return false;
            foreach (var ch in label)
            {
                var ok = ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
                if (!ok)
                    return false;
            }
        }
        return true;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var host = value.Trim().TrimEnd('.').ToLowerInvariant();
        return IsValid(host) ? host : null;
    }
}
