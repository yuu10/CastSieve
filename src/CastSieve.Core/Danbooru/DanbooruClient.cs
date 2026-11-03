using System.Text.Json;

namespace CastSieve.Danbooru;

public static class TagCategory
{
    public const int General = 0;
    public const int Artist = 1;
    public const int Copyright = 3;
    public const int Character = 4;
    public const int Meta = 5;
}

public sealed record DanbooruTag(string Name, int Category, int PostCount, bool IsDeprecated);

public sealed class DanbooruException : Exception
{
    public int StatusCode { get; }

    public DanbooruException(int statusCode, string message) : base(message) =>
        StatusCode = statusCode;
}

/// <summary>
/// Danbooru の公開 API (認証なしの読み取り) を使う最小クライアント。
/// ・タグは search[name_comma] でまとめて照会する。サーバーがこの検索条件を
///   受け付けない/無視する場合は、1タグずつ search[name_matches] で照会する方式に自動で切り替える。
/// ・リクエスト間隔を空け、429/5xx は待ってから再試行する。
/// </summary>
public sealed class DanbooruClient : IDisposable
{
    public const int BatchSize = 40;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private readonly TimeSpan _minInterval;
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private bool _batchSupported = true;

    public DanbooruClient(
        HttpMessageHandler? handler = null,
        string baseUrl = "https://danbooru.donmai.us",
        TimeSpan? minInterval = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CastSieve/1.0 (local image organizer)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _minInterval = minInterval ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>タグ名(小文字・アンダースコア区切り)を照会する。存在しないタグは結果に含まれない。</summary>
    public async Task<Dictionary<string, DanbooruTag>> GetTagsAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var result = new Dictionary<string, DanbooruTag>(StringComparer.Ordinal);

        foreach (var chunk in names.Distinct().Chunk(BatchSize))
        {
            if (_batchSupported)
            {
                var tags = await TryBatchAsync(chunk, ct);
                if (tags != null)
                {
                    foreach (var tag in tags) result[tag.Name] = tag;
                    continue;
                }
            }

            foreach (var name in chunk)
            {
                var tag = await GetSingleAsync(name, ct);
                if (tag != null) result[name] = tag;
            }
        }
        return result;
    }

    /// <summary>有効な別名 (antecedent → consequent) があれば、正規のタグ名を返す。</summary>
    public async Task<string?> GetAliasTargetAsync(string name, CancellationToken ct)
    {
        string url = "/tag_aliases.json"
                     + $"?search%5Bantecedent_name_matches%5D={Uri.EscapeDataString(name)}"
                     + "&search%5Bstatus%5D=active&limit=10";
        using var doc = await GetJsonAsync(url, ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? antecedent = GetString(e, "antecedent_name");
            string? consequent = GetString(e, "consequent_name");
            if (consequent != null && string.Equals(antecedent, name, StringComparison.OrdinalIgnoreCase))
                return consequent;
        }
        return null;
    }

    // ---- 照会 ---------------------------------------------------------------

    private async Task<List<DanbooruTag>?> TryBatchAsync(string[] chunk, CancellationToken ct)
    {
        string joined = string.Join(",", chunk.Select(Uri.EscapeDataString));
        try
        {
            using var doc = await GetJsonAsync($"/tags.json?search%5Bname_comma%5D={joined}&limit=1000", ct);
            var tags = ParseTagArray(doc);

            // 配列でない、または要求した数より多い = 検索条件が無視された。一括照会は使えないと判断する。
            if (tags == null || tags.Count > chunk.Length)
            {
                _batchSupported = false;
                return null;
            }
            return tags;
        }
        catch (DanbooruException ex) when (ex.StatusCode is 400 or 422)
        {
            _batchSupported = false;
            return null;
        }
    }

    private async Task<DanbooruTag?> GetSingleAsync(string name, CancellationToken ct)
    {
        string url = $"/tags.json?search%5Bname_matches%5D={Uri.EscapeDataString(name)}&limit=10";
        using var doc = await GetJsonAsync(url, ct);
        return ParseTagArray(doc)?.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<DanbooruTag>? ParseTagArray(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        var list = new List<DanbooruTag>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? name = GetString(e, "name");
            if (name == null) continue;

            list.Add(new DanbooruTag(
                name,
                GetInt(e, "category"),
                GetInt(e, "post_count"),
                e.TryGetProperty("is_deprecated", out var d) && d.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    private static string? GetString(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    // ---- HTTP ---------------------------------------------------------------

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            await ThrottleAsync(ct);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(relativeUrl, ct);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < 2) // タイムアウト
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }

                int code = (int)response.StatusCode;
                if ((code == 429 || code >= 500) && attempt < 3)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
                    if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
                    await Task.Delay(wait, ct);
                    continue;
                }

                throw new DanbooruException(code, $"Danbooru API がエラーを返しました (HTTP {code})");
            }
        }
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var wait = _lastRequestUtc + _minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _throttle.Dispose();
    }
}
