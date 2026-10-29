using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NtftEnhanced.Lists;

public sealed class StevenBlackClient : IListFetcher
{
    private readonly HttpClient _http;
    private readonly int _minimumDomains;

    public StevenBlackClient(HttpClient http, int minimumDomains = Product.ProductionMinimumDomains)
    {
        _http = http;
        _minimumDomains = minimumDomains;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NTFT-Enhanced", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ListFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        using var metaResponse = await _http.GetAsync(Product.ListApiUrl, cancellationToken).ConfigureAwait(false);
        if (!metaResponse.IsSuccessStatusCode)
            return ListFetchResult.Failed("Liste kaydı alınamadı.");

        var metaJson = await metaResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var meta = JsonSerializer.Deserialize<GitHubContent>(metaJson);
        if (meta?.Sha is null || meta.DownloadUrl is null || meta.Size is < 1 or > Product.MaxListBytes)
            return ListFetchResult.Failed("Liste kaydı eksik.");
        if (!meta.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ListFetchResult.Failed("Liste adresi HTTPS değil.");

        using var bodyResponse = await _http.GetAsync(meta.DownloadUrl, cancellationToken).ConfigureAwait(false);
        if (!bodyResponse.IsSuccessStatusCode)
            return ListFetchResult.Failed("Liste indirilemedi.");
        var bytes = await bodyResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var verdict = ListVerifier.Accept(bytes, meta.Sha, _minimumDomains);
        return verdict.Accepted
            ? ListFetchResult.Ready(bytes, meta.Sha)
            : ListFetchResult.Failed(verdict.Error ?? "Liste doğrulanamadı.");
    }

    private sealed record GitHubContent(
        [property: JsonPropertyName("sha")] string? Sha,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("download_url")] string? DownloadUrl);
}
