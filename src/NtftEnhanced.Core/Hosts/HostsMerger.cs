using System.Text;

namespace NtftEnhanced.Hosts;

public sealed record MergeResult(bool Success, string Text, string? Error)
{
    public static MergeResult Ok(string text) => new(true, text, null);
    public static MergeResult Fail(string error) => new(false, "", error);
}

public static class HostsMerger
{
    public static MergeResult Merge(string existing, IEnumerable<string> domains, IReadOnlySet<string> allowlist)
    {
        if (!TryExtractOutside(existing, out var outside, out var error))
            return MergeResult.Fail(error!);

        var block = BuildBlock(domains, allowlist);
        var merged = outside.TrimEnd() + "\r\n\r\n" + block;
        if (!PreservesLocalhost(existing, merged))
            return MergeResult.Fail("localhost satırı sonuçta yok.");
        if (BlockContainsCanary(block))
            return MergeResult.Fail("Kanarya alanı engellenecek.");
        return MergeResult.Ok(merged);
    }

    public static bool TryExtractOutside(string existing, out string outside, out string? error)
    {
        var begin = IndexOfMarker(existing, Product.HostsBegin);
        var end = IndexOfMarker(existing, Product.HostsEnd);
        if (begin < 0 && end < 0)
        {
            outside = existing;
            error = null;
            return true;
        }
        if (begin < 0 || end < 0 || end < begin)
        {
            outside = "";
            error = "Hosts işaretleri eksik veya ters.";
            return false;
        }
        if (existing.IndexOf(Product.HostsBegin, begin + Product.HostsBegin.Length, StringComparison.Ordinal) >= 0)
        {
            outside = "";
            error = "Hosts işaretleri yineleniyor.";
            return false;
        }

        var after = end + Product.HostsEnd.Length;
        outside = existing[..begin].TrimEnd() + "\r\n" + existing[after..].TrimStart('\r', '\n');
        error = null;
        return true;
    }

    public static string BuildBlock(IEnumerable<string> domains, IReadOnlySet<string> allowlist)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var domain in domains)
        {
            var host = DomainName.Normalize(domain);
            if (host is null || DomainName.IsCanary(host) || DomainName.IsInfrastructure(host))
                continue;
            if (allowlist.Contains(host))
                continue;
            selected.Add(host);
        }

        var builder = new StringBuilder();
        builder.Append(Product.HostsBegin);
        builder.Append("\r\n");
        builder.Append("# Kaynak: StevenBlack/hosts alternates/porn-only. MIT ve CC BY 4.0 karışık lisans.\r\n");
        foreach (var host in selected)
        {
            builder.Append("0.0.0.0 ");
            builder.Append(host);
            builder.Append("\r\n:: ");
            builder.Append(host);
            builder.Append("\r\n");
        }
        builder.Append(Product.HostsEnd);
        builder.Append("\r\n");
        return builder.ToString();
    }

    public static bool PreservesLocalhost(string before, string after)
    {
        foreach (var line in new[] { "127.0.0.1 localhost", "::1 localhost" })
        {
            if (before.Contains(line, StringComparison.OrdinalIgnoreCase) &&
                !after.Contains(line, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool BlockContainsCanary(string block)
    {
        foreach (var canary in Product.CanaryHosts)
        {
            if (block.Contains(" " + canary, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int IndexOfMarker(string text, string marker) =>
        text.IndexOf(marker, StringComparison.Ordinal);
}
