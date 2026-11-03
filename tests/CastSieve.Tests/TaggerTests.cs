using CastSieve.Sorting;
using CastSieve.Tagging;

namespace CastSieve.Tests;

internal sealed class FakeTagger : IImageTagger
{
    private readonly Func<string, IReadOnlyList<TaggerPrediction>> _predict;
    public List<string> Calls { get; } = new();

    public FakeTagger(Func<string, IReadOnlyList<TaggerPrediction>> predict) => _predict = predict;

    public IReadOnlyList<TaggerPrediction> PredictCharacters(string imagePath)
    {
        Calls.Add(Path.GetFileName(imagePath));
        return _predict(imagePath);
    }

    public void Dispose()
    {
    }
}

public class TaggerTests
{
    [Fact]
    public void Parses_tag_csv_with_quotes_and_keeps_row_positions()
    {
        var lines = new[]
        {
            "tag_id,name,category,count",
            "9999999,general,9,100",
            "470575,1girl,0,6000000",
            "1,\"a,b\",0,5",
            "2,frieren,4,12345",
            "broken line",
        };

        var tags = TaggerTagList.Parse(lines);

        Assert.Equal(5, tags.Count);
        Assert.Equal(new TaggerTag("general", 9), tags[0]);
        Assert.Equal(new TaggerTag("a,b", 0), tags[2]);
        Assert.Equal(new TaggerTag("frieren", 4), tags[3]);
        Assert.Equal(-1, tags[4].Category);
    }

    [Fact]
    public void SelectCharacters_applies_threshold_sorts_and_normalizes()
    {
        var tags = new List<TaggerTag>
        {
            new("1girl", 0),
            new("hatsune_miku", 4),
            new("rem_(re:zero)", 4),
            new("kafka_(honkai:_star_rail)", 4),
        };
        var scores = new[] { 0.99f, 0.90f, 0.97f, 0.40f };

        var picked = WdTagger.SelectCharacters(scores, tags, 0.85);

        Assert.Equal(
            new[] { "rem (re:zero)", "hatsune miku" },
            picked.Select(p => p.Tag).ToArray());
    }

    private static string Png(string dir, string name, params (string Key, string Text)[] meta)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, PromptExtractorTests.FakePng(meta));
        return path;
    }

    private static SortOptions Options(string root, string input, bool infer, string? modelsDir = null) => new()
    {
        InputDir = input,
        OutputDir = Path.Combine(root, "out"),
        Action = SortAction.Copy,
        UseDanbooru = false,
        InferFromImage = infer,
        ModelsDir = modelsDir,
        CachePath = Path.Combine(root, "cache.json"),
    };

    [Fact]
    public async Task Infers_characters_only_for_images_without_metadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ms_infer_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        try
        {
            Png(input, "with_meta.png", ("parameters", "rem (re:zero)\nNegative prompt: x\nSteps: 1"));
            Png(input, "no_meta.png");
            Png(input, "no_meta_nothing_found.png");

            var tagger = new FakeTagger(path =>
                Path.GetFileName(path) == "no_meta.png"
                    ? new[] { new TaggerPrediction("frieren", 0.95f) }
                    : Array.Empty<TaggerPrediction>());

            var report = await SortService.RunAsync(
                Options(root, input, infer: true), null, null, CancellationToken.None, _ => tagger);

            string output = Path.Combine(root, "out");
            Assert.True(File.Exists(Path.Combine(output, "rem (re_zero)", "with_meta.png")));
            Assert.True(File.Exists(Path.Combine(output, "frieren", "no_meta.png")));
            Assert.True(File.Exists(Path.Combine(output, FolderNames.Unknown, "no_meta_nothing_found.png")));

            // メタデータのある画像は推定にかけない
            Assert.DoesNotContain("with_meta.png", tagger.Calls);
            Assert.Equal(SortService.ImageInferredSource, report.Items.Single(i => i.Path.EndsWith("no_meta.png")).Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Predictions_below_the_threshold_become_candidates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ms_cand_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        try
        {
            Png(input, "sure.png");
            Png(input, "unsure.png");
            var tagger = new FakeTagger(path =>
                Path.GetFileName(path) == "sure.png"
                    ? new[] { new TaggerPrediction("frieren", 0.95f), new TaggerPrediction("fern (sousou no frieren)", 0.30f) }
                    : new[] { new TaggerPrediction("rem (re:zero)", 0.20f), new TaggerPrediction("ram (re:zero)", 0.40f) });

            var plan = await SortService.PlanAsync(
                Options(root, input, infer: true), null, null, CancellationToken.None, _ => tagger);

            var sure = plan.Items.Single(i => i.Path.EndsWith("sure.png") && !i.Path.EndsWith("unsure.png"));
            Assert.Equal("frieren", sure.Folders.Single());
            Assert.Equal(new[] { "fern (sousou no frieren)" }, sure.CandidateFolders);

            var unsure = plan.Items.Single(i => i.Path.EndsWith("unsure.png"));
            Assert.Equal(FolderNames.Unknown, unsure.Folders.Single());
            Assert.Empty(unsure.Characters);
            Assert.Equal(new[] { "ram (re_zero)", "rem (re_zero)" }, unsure.CandidateFolders); // 確信度の高い順
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Does_not_infer_when_the_option_is_off()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ms_off_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        try
        {
            Png(input, "no_meta.png");
            var tagger = new FakeTagger(_ => new[] { new TaggerPrediction("frieren", 0.95f) });

            await SortService.RunAsync(
                Options(root, input, infer: false), null, null, CancellationToken.None, _ => tagger);

            Assert.Empty(tagger.Calls);
            Assert.True(File.Exists(Path.Combine(root, "out", FolderNames.NoMetadata, "no_meta.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_model_gives_a_warning_and_keeps_the_old_behavior()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ms_nomodel_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        try
        {
            Png(input, "no_meta.png");

            var report = await SortService.RunAsync(
                Options(root, input, infer: true, modelsDir: Path.Combine(root, "models")),
                null, null, CancellationToken.None);

            Assert.NotNull(report.Warning);
            Assert.Contains("推定モデル", report.Warning);
            Assert.True(File.Exists(Path.Combine(root, "out", FolderNames.NoMetadata, "no_meta.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failing_prediction_keeps_the_image_in_no_metadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ms_fail_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        Directory.CreateDirectory(input);

        try
        {
            Png(input, "no_meta.png");
            var tagger = new FakeTagger(_ => throw new InvalidDataException("壊れた画像"));

            var report = await SortService.RunAsync(
                Options(root, input, infer: true), null, null, CancellationToken.None, _ => tagger);

            var item = report.Items.Single();
            Assert.Contains("壊れた画像", item.Error);
            Assert.Equal(FolderNames.NoMetadata, item.Folders.Single());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
