namespace NtftEnhanced.Lists;

public sealed record ListVerdict(bool Accepted, IReadOnlyList<string> Domains, string? Error);

public static class ListVerifier
{
    public static ListVerdict Accept(byte[] bytes, string expectedSha, int minimumDomains)
    {
        if (bytes.Length == 0 || bytes.Length > Product.MaxListBytes)
            return new(false, [], "Liste boyutu uygun değil.");

        var actual = Hosts.GitBlob.Sha1Hex(bytes);
        if (!Hosts.GitBlob.ShaMatches(expectedSha, actual))
            return new(false, [], "Liste özeti GitHub kaydıyla uyuşmuyor.");

        var parsed = Hosts.HostsListParser.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        if (!parsed.IsValid)
            return new(false, [], parsed.Error ?? "Liste ayrıştırılamadı.");
        if (parsed.Domains.Count < minimumDomains)
            return new(false, [], "Liste beklenenden kısa. Eski liste duruyor.");
        return new(true, parsed.Domains, null);
    }
}

public sealed record ListFetchResult(bool Success, byte[]? Bytes, string? Sha, string? Error)
{
    public static ListFetchResult Ready(byte[] bytes, string sha) => new(true, bytes, sha, null);
    public static ListFetchResult Failed(string error) => new(false, null, null, error);
}

public interface IListFetcher
{
    Task<ListFetchResult> FetchAsync(CancellationToken cancellationToken);
}
