using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CastSieve.Danbooru;

public sealed class CacheEntry
{
    /// <summary>Danbooru のカテゴリ。-1 は「Danbooru にこのタグは存在しない」。</summary>
    public int Category { get; set; } = -1;
    public int PostCount { get; set; }
    public bool Deprecated { get; set; }

    /// <summary>別名だった場合の正規タグ名。</summary>
    public string? CanonicalName { get; set; }
    public bool AliasChecked { get; set; }
    public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

    public static CacheEntry FromTag(DanbooruTag tag) => new()
    {
        Category = tag.Category,
        PostCount = tag.PostCount,
        Deprecated = tag.IsDeprecated,
    };

    public static CacheEntry NotFound() => new();
}

/// <summary>Danbooru の照会結果を JSON ファイルに保存するローカルキャッシュ。</summary>
public sealed class TagCache
{
    public static readonly TimeSpan FoundTtl = TimeSpan.FromDays(30);
    public static readonly TimeSpan MissTtl = TimeSpan.FromDays(3);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly Dictionary<string, CacheEntry> _map;
    private readonly object _lock = new();

    public TagCache(string path)
    {
        _path = path;
        _map = Load(path);
    }

    public static string DefaultPath() =>
        Path.Combine(AppPaths.DataRoot, "danbooru_tags.json");

    public bool TryGet(string name, [MaybeNullWhen(false)] out CacheEntry entry)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(name, out var e))
            {
                var ttl = e.Category >= 0 ? FoundTtl : MissTtl;
                if (DateTime.UtcNow - e.FetchedUtc < ttl)
                {
                    entry = e;
                    return true;
                }
            }
        }
        entry = null;
        return false;
    }

    public void Set(string name, CacheEntry entry)
    {
        lock (_lock) _map[name] = entry;
    }

    public void Update(string name, Action<CacheEntry> update)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(name, out var e)) update(e);
        }
    }

    public void Save()
    {
        string json;
        lock (_lock) json = JsonSerializer.Serialize(_map, JsonOptions);

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // キャッシュを保存できなくても処理は続行できる
        }
    }

    private static Dictionary<string, CacheEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(path));
                if (map != null) return new Dictionary<string, CacheEntry>(map, StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 壊れたキャッシュは捨てて作り直す
        }
        return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
    }
}
