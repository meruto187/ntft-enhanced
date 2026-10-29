namespace NtftEnhanced.Proxy;

public static class ConnectRequest
{
    public static bool TryGetHost(string header, out string host)
    {
        host = "";
        var lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
        var first = lineEnd >= 0 ? header[..lineEnd] : header;
        var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        if (parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            return TryHostPort(parts[1], out host);

        if (parts[1].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            parts[1].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(parts[1], UriKind.Absolute, out var uri))
            {
                host = uri.Host.TrimEnd('.').ToLowerInvariant();
                return host.Length > 0;
            }
        }

        var hostHeader = header.Split("\r\n").FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        if (hostHeader is null)
            return false;
        return TryHostPort(hostHeader[5..].Trim(), out host);
    }

    public static bool TryHostPort(string value, out string host)
    {
        host = "";
        var text = value.Trim();
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close <= 1)
                return false;
            host = text[1..close].ToLowerInvariant();
            return host.Length > 0;
        }

        var colon = text.LastIndexOf(':');
        host = (colon > 0 ? text[..colon] : text).TrimEnd('.').ToLowerInvariant();
        return host.Length > 0 && !host.Contains(' ');
    }
}

public static class BlockList
{
    public static bool Contains(string host, IReadOnlySet<string> blocked, IReadOnlySet<string> allowlist)
    {
        if (allowlist.Contains(host))
            return false;
        return blocked.Contains(host);
    }
}

public static class PacFile
{
    public static string Create(int? port)
    {
        if (port is null)
            return "function FindProxyForURL(url, host) { return \"DIRECT\"; }\r\n";

        return "function FindProxyForURL(url, host) {\r\n" +
               "  if (isPlainHostName(host) || host == \"localhost\" || host == \"127.0.0.1\") return \"DIRECT\";\r\n" +
               $"  return \"PROXY 127.0.0.1:{port}; DIRECT\";\r\n" +
               "}\r\n";
    }
}
