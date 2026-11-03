using System.Net;
using System.Text;
using CastSieve.Danbooru;

namespace CastSieve.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<Uri, string> _respond;
    public List<Uri> Requests { get; } = new();

    public FakeHandler(Func<Uri, string> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_respond(request.RequestUri!), Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

public class DanbooruTests
{
    /// <summary>クエリ文字列から search[...] パラメータの値を取り出す。</summary>
    private static string Query(Uri uri, string key)
    {
        string query = Uri.UnescapeDataString(uri.Query.TrimStart('?'));
        foreach (var part in query.Split('&'))
        {
            if (part.StartsWith(key + "=", StringComparison.Ordinal))
                return part[(key.Length + 1)..];
        }
        return "";
    }

    private static string TagJson(string name, int category, int posts) =>
        $"{{\"name\":\"{name}\",\"category\":{category},\"post_count\":{posts},\"is_deprecated\":false}}";

    private static string TempCachePath() =>
        Path.Combine(Path.GetTempPath(), $"cs_cache_{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Finds_character_tags_via_batch_query_and_alias()
    {
        var db = new Dictionary<string, (int Category, int Posts)>
        {
            ["frieren"] = (4, 5000),
            ["1girl"] = (0, 100000),
            ["solo"] = (0, 200000),
            ["ganyu_(genshin_impact)"] = (4, 9000),
        };

        string Respond(Uri uri)
        {
            if (uri.AbsolutePath == "/tags.json")
            {
                string names = Query(uri, "search[name_comma]");
                if (names.Length > 0)
                {
                    var hits = names.Split(',').Where(db.ContainsKey).Select(n => TagJson(n, db[n].Category, db[n].Posts));
                    return "[" + string.Join(",", hits) + "]";
                }
            }
            if (uri.AbsolutePath == "/tag_aliases.json")
            {
                return Query(uri, "search[antecedent_name_matches]") == "ganyu"
                    ? "[{\"antecedent_name\":\"ganyu\",\"consequent_name\":\"ganyu_(genshin_impact)\"}]"
                    : "[]";
            }
            return "[]";
        }

        using var client = new DanbooruClient(new FakeHandler(Respond), minInterval: TimeSpan.Zero);
        var resolver = new CharacterResolver(client, new TagCache(TempCachePath()), new ResolverOptions());

        var tags = new List<string> { "1girl", "frieren", "solo", "ganyu" };
        await resolver.PrefetchAsync(tags, null, CancellationToken.None);

        Assert.Equal(new[] { "frieren", "ganyu (genshin impact)" }, resolver.FindCharacterFolders(tags));
        Assert.False(resolver.NetworkDown);
    }

    [Fact]
    public async Task Falls_back_to_single_queries_when_the_batch_filter_is_ignored()
    {
        string Respond(Uri uri)
        {
            if (uri.AbsolutePath != "/tags.json") return "[]";

            if (Query(uri, "search[name_comma]").Length > 0)
            {
                // 検索条件が無視されて無関係なタグが大量に返ってくるケース
                return "[" + string.Join(",", Enumerable.Range(0, 100).Select(i => TagJson($"tag{i}", 0, 1))) + "]";
            }

            return Query(uri, "search[name_matches]") == "frieren" ? "[" + TagJson("frieren", 4, 5000) + "]" : "[]";
        }

        var handler = new FakeHandler(Respond);
        using var client = new DanbooruClient(handler, minInterval: TimeSpan.Zero);
        var resolver = new CharacterResolver(
            client, new TagCache(TempCachePath()), new ResolverOptions { ResolveAliases = false });

        var tags = new List<string> { "frieren", "1girl" };
        await resolver.PrefetchAsync(tags, null, CancellationToken.None);

        Assert.Equal(new[] { "frieren" }, resolver.FindCharacterFolders(tags));
    }

    [Fact]
    public async Task Uses_post_count_threshold()
    {
        string Respond(Uri uri) =>
            "[" + TagJson("obscure_character", 4, 3) + "," + TagJson("popular_character", 4, 5000) + "]";

        using var client = new DanbooruClient(new FakeHandler(Respond), minInterval: TimeSpan.Zero);
        var resolver = new CharacterResolver(
            client, new TagCache(TempCachePath()), new ResolverOptions { MinPostCount = 100, ResolveAliases = false });

        var tags = new List<string> { "obscure character", "popular character" };
        await resolver.PrefetchAsync(tags, null, CancellationToken.None);

        Assert.Equal(new[] { "popular character" }, resolver.FindCharacterFolders(tags));
    }

    [Fact]
    public void Offline_uses_series_heuristic_but_skips_cosplay()
    {
        var resolver = new CharacterResolver(null, new TagCache(TempCachePath()), new ResolverOptions());
        var tags = new List<string> { "rem (re:zero)", "hatsune miku (cosplay)", "bow (weapon)", "1girl" };
        Assert.Equal(new[] { "rem (re:zero)" }, resolver.FindCharacterFolders(tags));
    }

    [Fact]
    public void Overrides_take_priority()
    {
        var overrides = new Dictionary<string, string> { ["hatsune miku"] = "初音ミク" };
        var resolver = new CharacterResolver(null, new TagCache(TempCachePath()), new ResolverOptions(), overrides);
        Assert.Equal(new[] { "初音ミク" }, resolver.FindCharacterFolders(new List<string> { "hatsune miku" }));
    }
}
