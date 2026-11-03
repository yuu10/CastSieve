using System.Text.Json;
using System.Text.RegularExpressions;
using CastSieve.Tags;

namespace CastSieve.Danbooru;

public sealed record ResolveProgress(string Phase, int Done, int Total);

public sealed class ResolverOptions
{
    /// <summary>この投稿数に満たないキャラクタータグは無視する(誤検出対策)。</summary>
    public int MinPostCount { get; init; }

    /// <summary>Danbooru に存在しないタグについて、別名 (tag alias) から正規タグを探す。</summary>
    public bool ResolveAliases { get; init; } = true;

    /// <summary>Danbooru に問い合わせられなかったタグを「名前 (作品名)」形式で簡易判定する。</summary>
    public bool HeuristicWhenUnresolved { get; init; } = true;

    /// <summary>フォルダ名の空白を _ のままにする(Danbooru の表記)。</summary>
    public bool UnderscoreFolderNames { get; init; }
}

/// <summary>
/// タグがキャラクターかどうかを Danbooru のタグカテゴリ (4 = character) で判定する。
/// 判定の優先順位: 手動の上書きリスト → ローカルキャッシュ/Danbooru → (照会できなかった場合のみ)簡易判定。
/// </summary>
public sealed class CharacterResolver
{
    private static readonly Regex SeriesRe = new(@"^(.+?) \(([^()]+)\)$", RegexOptions.Compiled);

    private static readonly HashSet<string> NonCharacterQualifiers = new(StringComparer.Ordinal)
    {
        "cosplay", "style", "medium", "artist", "object", "weapon", "food", "fruit", "animal",
        "clothing", "symbol", "meme", "company", "brand", "unit", "creature", "instrument",
        "vehicle", "pattern", "fabric", "organ", "plant", "flower",
    };

    private readonly DanbooruClient? _client;
    private readonly TagCache _cache;
    private readonly ResolverOptions _options;
    private readonly IReadOnlyDictionary<string, string> _overrides;
    private int _failures;

    public bool NetworkDown { get; private set; }
    public string? LastError { get; private set; }

    public CharacterResolver(
        DanbooruClient? client,
        TagCache cache,
        ResolverOptions options,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        _client = client;
        _cache = cache;
        _options = options;
        _overrides = overrides ?? new Dictionary<string, string>();
    }

    /// <summary>キャッシュにないタグをまとめて Danbooru に照会し、結果をキャッシュに保存する。</summary>
    public async Task PrefetchAsync(IEnumerable<string> tags, IProgress<ResolveProgress>? progress, CancellationToken ct)
    {
        if (_client == null) return;

        var names = tags
            .Where(t => !_overrides.ContainsKey(t))
            .Select(TagParser.ToDanbooruName)
            .Where(IsQueryable)
            .Distinct()
            .ToList();

        var missing = names.Where(n => !_cache.TryGet(n, out _)).ToList();
        await FetchAndCacheAsync(missing, "Danbooruでタグを照会", progress, ct);

        if (_options.ResolveAliases && !NetworkDown)
            await ResolveAliasesAsync(names, progress, ct);

        _cache.Save();
    }

    /// <summary>タグ一覧(正規化済み)から、キャラクターのフォルダ名を出現順・重複なしで返す。</summary>
    public List<string> FindCharacterFolders(IReadOnlyList<string> tags)
    {
        var folders = new List<string>();
        foreach (var tag in tags)
        {
            string? folder = Classify(tag);
            if (folder != null && !folders.Contains(folder))
                folders.Add(folder);
        }
        return folders;
    }

    /// <summary>
    /// 画像から推定したキャラクター(Danbooru のタグ名そのもの)のフォルダ名を返す。
    /// すでにキャラクターと分かっているので Danbooru へは問い合わせず、上書きリストだけ反映する。
    /// </summary>
    public string FolderForKnownCharacter(string tag)
    {
        if (_overrides.TryGetValue(tag, out var overrideFolder))
            return overrideFolder;
        return _options.UnderscoreFolderNames ? tag.Replace(' ', '_') : tag;
    }

    private string? Classify(string tag)
    {
        if (_overrides.TryGetValue(tag, out var overrideFolder))
            return overrideFolder;

        string name = TagParser.ToDanbooruName(tag);
        if (!IsQueryable(name)) return null;

        if (!_cache.TryGet(name, out var entry))
            return _options.HeuristicWhenUnresolved ? Heuristic(tag) : null;

        // 別名なら正規タグに置き換えて判定する
        if (entry.Category < 0 && entry.CanonicalName != null)
        {
            name = entry.CanonicalName;
            if (!_cache.TryGet(name, out entry)) return null;
        }

        if (entry.Category == TagCategory.Character
            && entry.PostCount >= _options.MinPostCount
            && !name.EndsWith("_(cosplay)", StringComparison.Ordinal))
        {
            return _options.UnderscoreFolderNames ? name : name.Replace('_', ' ');
        }
        return null;
    }

    private string? Heuristic(string tag)
    {
        var m = SeriesRe.Match(tag);
        if (!m.Success || NonCharacterQualifiers.Contains(m.Groups[2].Value.Trim())) return null;
        return _options.UnderscoreFolderNames ? tag.Replace(' ', '_') : tag;
    }

    // ---- 照会処理 -----------------------------------------------------------

    private async Task FetchAndCacheAsync(
        List<string> names, string phase, IProgress<ResolveProgress>? progress, CancellationToken ct)
    {
        int done = 0;
        foreach (var chunk in names.Chunk(DanbooruClient.BatchSize))
        {
            if (NetworkDown) break;
            ct.ThrowIfCancellationRequested();

            try
            {
                var found = await _client!.GetTagsAsync(chunk, ct);
                foreach (var n in chunk)
                    _cache.Set(n, found.TryGetValue(n, out var tag) ? CacheEntry.FromTag(tag) : CacheEntry.NotFound());
                _failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                RegisterFailure(ex);
            }

            done += chunk.Length;
            progress?.Report(new ResolveProgress(phase, done, names.Count));
        }
    }

    private async Task ResolveAliasesAsync(List<string> names, IProgress<ResolveProgress>? progress, CancellationToken ct)
    {
        var unresolved = names
            .Where(n => _cache.TryGet(n, out var e) && e.Category < 0 && !e.AliasChecked)
            .ToList();

        var targets = new List<string>();
        int done = 0;

        foreach (var name in unresolved)
        {
            if (NetworkDown) break;
            ct.ThrowIfCancellationRequested();

            try
            {
                string? target = await _client!.GetAliasTargetAsync(name, ct);
                _cache.Update(name, e =>
                {
                    e.AliasChecked = true;
                    e.CanonicalName = target;
                });
                if (target != null && !_cache.TryGet(target, out _)) targets.Add(target);
                _failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (DanbooruException ex) when (ex.StatusCode is 400 or 422)
            {
                LastError = "別名の照会がサーバーに受け付けられませんでした: " + ex.Message;
                break; // 別名解決だけ諦める(タグ照会の結果は有効)
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                RegisterFailure(ex);
            }

            progress?.Report(new ResolveProgress("別名を確認", ++done, unresolved.Count));
        }

        await FetchAndCacheAsync(targets.Distinct().ToList(), "別名先のタグを照会", progress, ct);
    }

    private static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or DanbooruException or JsonException or TaskCanceledException;

    private void RegisterFailure(Exception ex)
    {
        LastError = ex.Message;
        if (++_failures >= 3) NetworkDown = true;
    }

    /// <summary>文章のような長いタグや、Danbooru のタグ名として使えない文字を含むものは照会しない。</summary>
    private static bool IsQueryable(string name)
    {
        if (name.Length == 0 || name.Length > 100) return false;
        if (name.IndexOfAny(new[] { '*', ',', '%', '\\', '|', '#' }) >= 0) return false;
        return !name.All(char.IsDigit);
    }
}
