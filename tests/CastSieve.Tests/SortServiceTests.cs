using CastSieve.Sorting;

namespace CastSieve.Tests;

public class SortServiceTests
{
    [Fact]
    public void Sanitize_replaces_invalid_characters()
    {
        Assert.Equal("kafka (honkai_ star rail)", FolderNames.Sanitize("kafka (honkai: star rail)"));
        Assert.Equal("_CON", FolderNames.Sanitize("CON")); // Windows の予約名
    }

    [Fact]
    public async Task Sorts_files_offline_with_overrides_and_heuristic()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cs_sort_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);

        try
        {
            File.WriteAllBytes(Path.Combine(input, "a.png"),
                PromptExtractorTests.FakePng(("parameters", "1girl, frieren, solo\nNegative prompt: x\nSteps: 1")));
            File.WriteAllBytes(Path.Combine(input, "b.png"),
                PromptExtractorTests.FakePng(("parameters", "rem (re:zero), 1girl\nNegative prompt: x\nSteps: 1")));
            File.WriteAllBytes(Path.Combine(input, "c.png"), PromptExtractorTests.FakePng());
            File.WriteAllBytes(Path.Combine(input, "d.png"),
                PromptExtractorTests.FakePng(("parameters", "landscape, sky\nNegative prompt: x\nSteps: 1")));

            string overrides = Path.Combine(root, "overrides.csv");
            File.WriteAllText(overrides, "frieren,フリーレン\n");

            var options = new SortOptions
            {
                InputDir = input,
                OutputDir = output,
                Action = SortAction.Copy,
                UseDanbooru = false,
                OverridesPath = overrides,
                CachePath = Path.Combine(root, "cache.json"),
            };

            var report = await SortService.RunAsync(options, null, null, CancellationToken.None);

            Assert.Equal(4, report.Items.Count);
            Assert.True(File.Exists(Path.Combine(output, "フリーレン", "a.png")));
            Assert.True(File.Exists(Path.Combine(output, "rem (re_zero)", "b.png")));
            Assert.True(File.Exists(Path.Combine(output, FolderNames.NoMetadata, "c.png")));
            Assert.True(File.Exists(Path.Combine(output, FolderNames.Unknown, "d.png")));
            Assert.True(File.Exists(Path.Combine(input, "a.png"))); // コピーなので元は残る
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_does_not_touch_the_file_system()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cs_prev_{Guid.NewGuid():N}");
        string input = Path.Combine(root, "in");
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);

        try
        {
            File.WriteAllBytes(Path.Combine(input, "a.png"),
                PromptExtractorTests.FakePng(("parameters", "rem (re:zero)\nNegative prompt: x\nSteps: 1")));

            var options = new SortOptions
            {
                InputDir = input,
                OutputDir = output,
                Action = SortAction.Preview,
                UseDanbooru = false,
                CachePath = Path.Combine(root, "cache.json"),
            };

            var report = await SortService.RunAsync(options, null, null, CancellationToken.None);

            Assert.Single(report.Items);
            Assert.Equal("rem (re_zero)", report.Items[0].Folders[0]);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
