using System.Runtime.CompilerServices;
using System.Text;
using CastSieve.Danbooru;
using CastSieve.Metadata;
using CastSieve.Tagging;
using CastSieve.Tags;

namespace CastSieve.Sorting;

/// <summary>
/// 仕分けの流れ(解析と実行は別の操作):
///   解析 (<see cref="PlanAsync"/>):
///     1) 画像からプロンプトを読み取りタグに分解
///     2) 未知のタグをまとめて Danbooru に照会(結果はキャッシュ)
///     3) メタデータがない画像と、タグからキャラクターが見つからなかった画像は、必要なら画像から推定
///     4) キャラクターを判定して仕分け先を決める(ファイルは変更しない)
///   実行 (<see cref="ExecuteAsync"/>):
///     解析の結果のとおりにコピー/移動する(再解析しない)
/// </summary>
public static class SortService
{
    /// <summary>画像から推定してキャラクターを決めた画像の SortItem.Source。</summary>
    public const string ImageInferredSource = "image-ai";

    /// <summary>しきい値に届かなかったキャラクターを、候補として何人まで残すか。</summary>
    private const int MaxCandidates = 5;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>
    /// 解析と実行をまとめて行う (テストや一括処理向け)。プレビューなら解析だけ行い、ファイルは変更しない。
    /// </summary>
    public static async Task<SortReport> RunAsync(
        SortOptions options,
        IProgress<SortProgress>? progress,
        IProgress<SortItem>? itemProgress,
        CancellationToken ct,
        Func<SortOptions, IImageTagger?>? taggerFactory = null)
    {
        var plan = await PlanAsync(options, progress, itemProgress, ct, taggerFactory);
        if (options.Action == SortAction.Preview)
        {
            var preview = new SortReport { Warning = plan.Warning };
            preview.Items.AddRange(plan.Items);
            return preview;
        }
        return await ExecuteAsync(plan, options.Action, progress, itemProgress, ct);
    }

    /// <summary>
    /// 解析: 画像を読み取り、キャラクターを判別して、どの画像をどのフォルダに仕分けるかを決める。
    /// ファイルは変更しない。結果の <see cref="SortPlan"/> を <see cref="ExecuteAsync"/> に渡すと、再解析せずに実行できる。
    /// </summary>
    /// <param name="taggerFactory">推定器を差し替える(テスト用)。null なら <see cref="WdTagger"/> を使う。</param>
    public static async Task<SortPlan> PlanAsync(
        SortOptions options,
        IProgress<SortProgress>? progress,
        IProgress<SortItem>? itemProgress,
        CancellationToken ct,
        Func<SortOptions, IImageTagger?>? taggerFactory = null)
    {
        var plan = new SortPlan { Options = options };

        // 1) 読み取り
        var files = await Task.Run(
            () => Scan(options).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(), ct);

        var extracted = new List<(SortItem Item, List<string> Tags)>(files.Count);
        await Task.Run(() =>
        {
            int i = 0;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;
                var info = PromptExtractor.Extract(file);
                var item = new SortItem { Path = file, Source = info.Source };
                var tags = info.Prompt.Length > 0 ? TagParser.Split(info.Prompt) : new List<string>();
                extracted.Add((item, tags));
                progress?.Report(new SortProgress("メタデータを読み取り", ++i, files.Count));
            }
        }, ct);
        ct.ThrowIfCancellationRequested(); // 途中で中止したときの例外は、デバッガーが止まらないよう呼び出し側で投げる

        // 2) Danbooru 照会
        using var client = options.UseDanbooru ? new DanbooruClient() : null;
        var cache = new TagCache(options.CachePath ?? TagCache.DefaultPath());
        var resolver = new CharacterResolver(
            client,
            cache,
            new ResolverOptions
            {
                MinPostCount = options.MinPostCount,
                ResolveAliases = options.ResolveAliases,
                UnderscoreFolderNames = options.UnderscoreFolderNames,
            },
            OverrideFile.Load(options.OverridesPath));

        var allTags = extracted.SelectMany(e => e.Tags).Distinct().ToList();
        await resolver.PrefetchAsync(
            allTags,
            new Progress<ResolveProgress>(p => progress?.Report(new SortProgress(p.Phase, p.Done, p.Total))),
            ct);

        // 2.5) 画像からの推定(メタデータがない画像と、必要ならタグからキャラクターが見つからなかった画像)
        //      タグで見つかったかどうかを使うので、Danbooru 照会の後に行う
        var warnings = new List<string>();
        if (options.InferFromImage)
        {
            var targets = extracted
                .Where(e => e.Item.Source == PromptInfo.None // ファイルを読めなかった画像 (error) は推定しても読めない
                    || (options.InferUnknown && e.Item.HasMetadata && resolver.FindCharacterFolders(e.Tags).Count == 0))
                .Select(e => e.Item)
                .ToList();
            await InferAsync(options, taggerFactory, targets, progress, warnings, plan.Notes, ct);
        }

        // 3) 仕分け先を決める(ファイルは触らない)
        await Task.Run(() =>
        {
            int i = 0;
            foreach (var (item, tags) in extracted)
            {
                if (ct.IsCancellationRequested) break;
                Assign(item, tags, resolver, options);
                plan.Items.Add(item);
                itemProgress?.Report(item);
                progress?.Report(new SortProgress("仕分け先を決定", ++i, extracted.Count));
            }
        }, ct);
        ct.ThrowIfCancellationRequested(); // 途中で中止したときの例外は、デバッガーが止まらないよう呼び出し側で投げる

        if (options.UseDanbooru && resolver.LastError != null)
        {
            warnings.Add(resolver.NetworkDown
                ? $"Danbooruに接続できなかったため、一部は簡易判定になっています ({resolver.LastError})"
                : $"Danbooruへの問い合わせで一部エラーがありました ({resolver.LastError})");
        }
        if (warnings.Count > 0)
            plan.Warning = string.Join(" / ", warnings);
        return plan;
    }

    /// <summary>
    /// 実行: 解析の結果 (<see cref="SortPlan"/>) のとおりに、画像をコピーまたは移動する。再解析はしない。
    /// 対象外の画像は何もしない。元のファイルがなくなっていた画像は失敗として記録して続行する。
    /// 同じ結果は2度実行できない (途中で中止した場合も)。
    /// </summary>
    public static async Task<SortReport> ExecuteAsync(
        SortPlan plan,
        SortAction action,
        IProgress<SortProgress>? progress,
        IProgress<SortItem>? itemProgress,
        CancellationToken ct)
    {
        if (action == SortAction.Preview)
            throw new ArgumentException("実行には Copy か Move を指定してください。", nameof(action));
        if (plan.Executed)
            throw new InvalidOperationException("この解析結果はすでに実行済みです。もう一度解析してください。");

        var targets = plan.Items.Where(i => !i.Skipped).ToList();
        var report = new SortReport { Warning = plan.Warning };
        string phase = action == SortAction.Move ? "移動" : "コピー";

        try
        {
            await Task.Run(() =>
            {
                int done = 0;
                foreach (var item in targets)
                {
                    if (ct.IsCancellationRequested) break;
                    item.Destinations.Clear();
                    try
                    {
                        if (!File.Exists(item.Path))
                            throw new FileNotFoundException("元のファイルが見つかりません");
                        Apply(item, plan.Options.OutputDir, action);
                        item.Status = action == SortAction.Move ? "移動済" : "コピー済";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        item.Error = string.IsNullOrEmpty(item.Error) ? ex.Message : item.Error + " / " + ex.Message;
                        item.Status = "失敗: " + ex.Message;
                    }
                    report.Items.Add(item);
                    itemProgress?.Report(item);
                    progress?.Report(new SortProgress(phase, ++done, targets.Count));
                }
            }, ct);
            ct.ThrowIfCancellationRequested(); // 途中で中止したときの例外は、デバッガーが止まらないよう呼び出し側で投げる
        }
        finally
        {
            // 途中で中止しても一部は処理済みなので、同じ結果をもう一度実行することはできない
            plan.Executed = true;
            if (report.Items.Count > 0)
                WriteLog(plan.Options, report.Items);
        }
        return report;
    }

    private static async Task InferAsync(
        SortOptions options,
        Func<SortOptions, IImageTagger?>? factory,
        List<SortItem> targets,
        IProgress<SortProgress>? progress,
        List<string> warnings,
        List<string> notes,
        CancellationToken ct)
    {
        if (targets.Count == 0) return;

        var model = ModelCatalog.Find(options.TaggerModelId);
        if (factory == null && !ModelCatalog.IsInstalled(model, options.ModelsDir))
        {
            warnings.Add($"推定モデルが見つからないため、画像からの推定は行いませんでした ({ModelCatalog.DirectoryFor(model, options.ModelsDir)})");
            return;
        }

        IImageTagger? tagger;
        try
        {
            tagger = factory != null ? factory(options) : CreateDefaultTagger(options, model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ネイティブライブラリの読み込み失敗など、種類が多いので境界でまとめて扱う
            warnings.Add($"推定モデルを読み込めませんでした ({ex.Message})");
            return;
        }
        if (tagger == null) return;

        double threshold = Math.Clamp(options.CharacterThreshold, 0.05, 0.99);
        using (tagger)
        {
            await Task.Run(() =>
            {
                int done = 0;
                foreach (var item in targets)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        foreach (var prediction in tagger.PredictCharacters(item.Path).OrderByDescending(p => p.Score))
                        {
                            if (prediction.Score >= threshold)
                                item.InferredCharacters.Add(prediction);
                            else if (item.CandidateCharacters.Count < MaxCandidates)
                                item.CandidateCharacters.Add(prediction);
                        }
                        // 推定できた画像は「メタデータなし」ではなくなる。メタデータのある画像は形式名を残す
                        item.Source = item.Source == PromptInfo.None
                            ? ImageInferredSource
                            : $"{item.Source} + {ImageInferredSource}";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        item.Error = "画像からの推定に失敗: " + ex.Message;
                    }
                    progress?.Report(new SortProgress("画像からキャラクターを推定", ++done, targets.Count));
                }
            }, ct);
            ct.ThrowIfCancellationRequested(); // 途中で中止したときの例外は、デバッガーが止まらないよう呼び出し側で投げる

            if (tagger.Description != null) notes.Add($"画像からの推定: {tagger.Description}");
            if (tagger.Warning != null) warnings.Add(tagger.Warning);
        }
    }

    // OnnxRuntime への依存をこのメソッドに閉じ込める(推定を使わないときはネイティブライブラリを読み込まない)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IImageTagger CreateDefaultTagger(SortOptions options, TaggerModel model) =>
        new WdTagger(ModelCatalog.DirectoryFor(model, options.ModelsDir), options.UseGpu);

    private static IEnumerable<string> Scan(SortOptions options)
    {
        string outFull = Path.GetFullPath(options.OutputDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        foreach (var file in Directory.EnumerateFiles(options.InputDir, "*", enumeration))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(file))) continue;
            if (Path.GetFullPath(file).StartsWith(outFull, StringComparison.OrdinalIgnoreCase)) continue; // 出力先は対象外
            yield return file;
        }
    }

    private static void Assign(SortItem item, List<string> tags, CharacterResolver resolver, SortOptions options)
    {
        if (!item.HasMetadata)
        {
            item.Folders.Add(FolderNames.NoMetadata);
            MarkTarget(item, options);
            return;
        }

        var found = resolver.FindCharacterFolders(tags);
        foreach (var inferred in item.InferredCharacters) // 確信度の高い順に並んでいる
        {
            string folder = resolver.FolderForKnownCharacter(inferred.Tag);
            if (!found.Contains(folder)) found.Add(folder);
        }
        var characters = found
            .Select(FolderNames.Sanitize)
            .Distinct()
            .ToList();
        item.Characters.AddRange(characters);
        item.CandidateFolders.AddRange(item.CandidateCharacters
            .Select(c => FolderNames.Sanitize(resolver.FolderForKnownCharacter(c.Tag)))
            .Where(f => !characters.Contains(f))
            .Distinct());

        if (characters.Count == 0)
            item.Folders.Add(FolderNames.Unknown);
        else if (characters.Count == 1 || options.Multi == MultiMode.FirstOnly)
            item.Folders.Add(characters[0]);
        else if (options.Multi == MultiMode.EachFolder)
            item.Folders.AddRange(characters);
        else
            item.Folders.Add(FolderNames.Multiple);

        MarkTarget(item, options);
    }

    /// <summary>_unknown / _no_metadata に入る画像を、オプションに応じて実行の対象外にする。</summary>
    private static void MarkTarget(SortItem item, SortOptions options)
    {
        bool single = item.Folders.Count == 1;
        item.Skipped =
            (single && item.Folders[0] == FolderNames.Unknown && !options.IncludeUnknown)
            || (single && item.Folders[0] == FolderNames.NoMetadata && !options.IncludeNoMetadata);
        item.Status = item.Skipped ? "対象外" : "実行待ち";
    }

    private static void Apply(SortItem item, string outputDir, SortAction action)
    {
        for (int i = 0; i < item.Folders.Count; i++)
        {
            string targetDir = Path.Combine(outputDir, item.Folders[i]);
            Directory.CreateDirectory(targetDir);
            string dest = UniquePath(Path.Combine(targetDir, Path.GetFileName(item.Path)));

            bool last = i == item.Folders.Count - 1;
            if (action == SortAction.Move && last)
                File.Move(item.Path, dest);
            else
                File.Copy(item.Path, dest);
            item.Destinations.Add(dest);
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem}_{n}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void WriteLog(SortOptions options, List<SortItem> items)
    {
        Directory.CreateDirectory(options.OutputDir);
        string path = Path.Combine(options.OutputDir, $"sort_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("source_file,metadata_type,characters,destinations,error");
        foreach (var it in items)
        {
            writer.WriteLine(string.Join(",",
                Csv(it.Path),
                Csv(it.Source),
                Csv(string.Join(" | ", it.Characters)),
                Csv(string.Join(" | ", it.Destinations)),
                Csv(it.Error)));
        }
    }

    private static string Csv(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
