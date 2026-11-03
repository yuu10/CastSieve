namespace CastSieve;

/// <summary>設定・キャッシュ・モデルを置くフォルダ (%LOCALAPPDATA%\CastSieve)。</summary>
public static class AppPaths
{
    public const string FolderName = "CastSieve";

    /// <summary>1.6.0 で改名する前のフォルダ名 (M Sorter)。</summary>
    public const string OldFolderName = "MSorter";

    public static string DataRoot => DataRootIn(LocalAppData);

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataRootIn(string baseDir) => Path.Combine(baseDir, FolderName);

    /// <summary>
    /// 旧フォルダがあり、新フォルダがまだないときだけ、旧フォルダを新しい名前に移す。
    /// 失敗しても起動は止めない(新しいフォルダで最初からやり直すだけ)。
    /// </summary>
    public static bool MigrateFromOldName() => MigrateFromOldName(LocalAppData);

    public static bool MigrateFromOldName(string baseDir)
    {
        string oldDir = Path.Combine(baseDir, OldFolderName);
        string newDir = DataRootIn(baseDir);
        if (!Directory.Exists(oldDir) || Directory.Exists(newDir)) return false;
        try
        {
            Directory.Move(oldDir, newDir); // 同じドライブ内なのでモデル(数百MB)もすぐ終わる
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
