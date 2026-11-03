namespace CastSieve.Tests;

public class AppPathsTests
{
    [Fact]
    public void Migrates_old_folder_when_new_one_is_missing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cs_paths_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MSorter", "models"));
            File.WriteAllText(Path.Combine(root, "MSorter", "settings.json"), "{}");

            Assert.True(AppPaths.MigrateFromOldName(root));
            Assert.False(Directory.Exists(Path.Combine(root, "MSorter")));
            Assert.True(File.Exists(Path.Combine(AppPaths.DataRootIn(root), "settings.json")));
            Assert.True(Directory.Exists(Path.Combine(AppPaths.DataRootIn(root), "models")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Keeps_both_folders_when_new_one_already_exists()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cs_paths_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MSorter"));
            Directory.CreateDirectory(AppPaths.DataRootIn(root));

            Assert.False(AppPaths.MigrateFromOldName(root));
            Assert.True(Directory.Exists(Path.Combine(root, "MSorter")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Does_nothing_without_old_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cs_paths_{Guid.NewGuid():N}");
        Assert.False(AppPaths.MigrateFromOldName(root));
        Assert.False(Directory.Exists(AppPaths.DataRootIn(root)));
    }
}
