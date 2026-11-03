using CastSieve.Sorting;

namespace CastSieve.Tests;

public class SortPlanTests
{
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ms_plan_{Guid.NewGuid():N}");
        public string Input => Path.Combine(Root, "in");
        public string Output => Path.Combine(Root, "out");

        public Workspace() => Directory.CreateDirectory(Input);

        public string Png(string name, params (string Key, string Text)[] meta)
        {
            string path = Path.Combine(Input, name);
            File.WriteAllBytes(path, PromptExtractorTests.FakePng(meta));
            return path;
        }

        public string Prompt(string name, string prompt) =>
            Png(name, ("parameters", prompt + "\nNegative prompt: x\nSteps: 1"));

        public SortOptions Options(bool includeUnknown = true, bool includeNoMetadata = true) => new()
        {
            InputDir = Input,
            OutputDir = Output,
            UseDanbooru = false,
            IncludeUnknown = includeUnknown,
            IncludeNoMetadata = includeNoMetadata,
            CachePath = Path.Combine(Root, "cache.json"),
        };

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public async Task Planning_does_not_touch_files_and_execution_applies_the_plan()
    {
        using var ws = new Workspace();
        ws.Prompt("a.png", "rem (re:zero)");
        ws.Png("b.png");

        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(2, plan.TargetCount);
        Assert.False(Directory.Exists(ws.Output)); // 解析ではファイルを作らない
        Assert.All(plan.Items, i => Assert.Equal("実行待ち", i.Status));

        var report = await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        Assert.Equal(2, report.Items.Count);
        Assert.True(File.Exists(Path.Combine(ws.Output, "rem (re_zero)", "a.png")));
        Assert.True(File.Exists(Path.Combine(ws.Output, FolderNames.NoMetadata, "b.png")));
        Assert.True(File.Exists(Path.Combine(ws.Input, "a.png"))); // コピーなので元は残る
        Assert.All(plan.Items, i => Assert.Equal("コピー済", i.Status));
        Assert.True(plan.Executed);
        Assert.Single(Directory.GetFiles(ws.Output, "sort_log_*.csv"));
    }

    [Fact]
    public async Task Execution_follows_the_plan_even_if_the_files_changed_after_analysis()
    {
        using var ws = new Workspace();
        ws.Prompt("a.png", "rem (re:zero)");

        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);

        // 解析のあとでメタデータを書き換えても、再解析せずに解析結果の仕分け先へ入る
        ws.Prompt("a.png", "ram (re:zero)");
        await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(ws.Output, "rem (re_zero)", "a.png")));
        Assert.False(Directory.Exists(Path.Combine(ws.Output, "ram (re_zero)")));
    }

    [Fact]
    public async Task A_plan_can_be_executed_only_once()
    {
        using var ws = new Workspace();
        ws.Prompt("a.png", "rem (re:zero)");

        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);
        await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None));
        Assert.Single(Directory.GetFiles(Path.Combine(ws.Output, "rem (re_zero)")));
    }

    [Fact]
    public async Task Preview_is_not_a_valid_action_for_execution()
    {
        using var ws = new Workspace();
        ws.Prompt("a.png", "rem (re:zero)");
        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            SortService.ExecuteAsync(plan, SortAction.Preview, null, null, CancellationToken.None));
        Assert.False(plan.Executed);
    }

    [Fact]
    public async Task Unknown_and_no_metadata_images_can_be_excluded_from_the_targets()
    {
        using var ws = new Workspace();
        ws.Prompt("char.png", "rem (re:zero)");
        ws.Prompt("unknown.png", "landscape, sky");
        ws.Png("nometa.png");

        var plan = await SortService.PlanAsync(
            ws.Options(includeUnknown: false, includeNoMetadata: false), null, null, CancellationToken.None);

        Assert.Equal(3, plan.Items.Count);
        Assert.Equal(1, plan.TargetCount);
        Assert.Equal("対象外", plan.Items.Single(i => i.Path.EndsWith("unknown.png")).Status);
        Assert.Equal("対象外", plan.Items.Single(i => i.Path.EndsWith("nometa.png")).Status);

        await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(ws.Output, "rem (re_zero)", "char.png")));
        Assert.False(Directory.Exists(Path.Combine(ws.Output, FolderNames.Unknown)));
        Assert.False(Directory.Exists(Path.Combine(ws.Output, FolderNames.NoMetadata)));
    }

    [Fact]
    public async Task Nothing_to_execute_when_every_image_is_excluded()
    {
        using var ws = new Workspace();
        ws.Png("nometa.png");

        var plan = await SortService.PlanAsync(
            ws.Options(includeNoMetadata: false), null, null, CancellationToken.None);

        Assert.Single(plan.Items);
        Assert.Equal(0, plan.TargetCount); // 画面では「この結果で実行」を押せなくなる
    }

    [Fact]
    public async Task A_file_that_disappeared_after_analysis_fails_alone()
    {
        using var ws = new Workspace();
        ws.Prompt("a.png", "rem (re:zero)");
        string b = ws.Prompt("b.png", "ram (re:zero)");

        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);
        File.Delete(b);

        var report = await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        var failed = report.Items.Single(i => i.Path.EndsWith("b.png"));
        Assert.StartsWith("失敗", failed.Status);
        Assert.NotEmpty(failed.Error);
        Assert.Equal("コピー済", report.Items.Single(i => i.Path.EndsWith("a.png")).Status);
        Assert.True(File.Exists(Path.Combine(ws.Output, "rem (re_zero)", "a.png")));
    }

    [Fact]
    public async Task Manually_assigned_images_go_to_the_chosen_folder_even_if_they_were_excluded()
    {
        using var ws = new Workspace();
        ws.Prompt("char.png", "rem (re:zero)");
        ws.Prompt("unknown.png", "landscape, sky");

        var plan = await SortService.PlanAsync(ws.Options(includeUnknown: false), null, null, CancellationToken.None);
        var unknown = plan.Items.Single(i => i.Path.EndsWith("unknown.png"));
        Assert.Equal(1, plan.TargetCount);

        string name = plan.AssignManually(new[] { unknown }, "my:oc");

        Assert.Equal("my_oc", name); // フォルダに使えない文字は直す
        Assert.True(unknown.IsManual);
        Assert.False(unknown.Skipped);
        Assert.Equal(2, plan.TargetCount);
        Assert.Contains("my_oc", plan.CharacterFolders());

        await SortService.ExecuteAsync(plan, SortAction.Copy, null, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(ws.Output, "my_oc", "unknown.png")));
        Assert.Throws<InvalidOperationException>(() => plan.AssignManually(new[] { unknown }, "other"));
    }

    [Fact]
    public async Task Resetting_restores_the_folders_and_exclusion_from_the_analysis()
    {
        using var ws = new Workspace();
        ws.Prompt("unknown.png", "landscape, sky");

        var plan = await SortService.PlanAsync(ws.Options(includeUnknown: false), null, null, CancellationToken.None);
        var item = plan.Items.Single();
        plan.AssignManually(new[] { item }, "first");
        plan.AssignManually(new[] { item }, "second"); // 2回指定しても、戻す先は解析の結果

        plan.ResetToAutomatic(new[] { item });

        Assert.False(item.IsManual);
        Assert.Equal(FolderNames.Unknown, item.Folders.Single());
        Assert.True(item.Skipped);
        Assert.Equal("対象外", item.Status);
        Assert.Equal(0, plan.TargetCount);
    }

    [Fact]
    public async Task Move_removes_the_source_and_records_the_destination()
    {
        using var ws = new Workspace();
        string a = ws.Prompt("a.png", "rem (re:zero)");

        var plan = await SortService.PlanAsync(ws.Options(), null, null, CancellationToken.None);
        await SortService.ExecuteAsync(plan, SortAction.Move, null, null, CancellationToken.None);

        string dest = Path.Combine(ws.Output, "rem (re_zero)", "a.png");
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(dest));
        Assert.Equal(dest, plan.Items.Single().Destinations.Single());
        Assert.Equal("移動済", plan.Items.Single().Status);
    }
}
