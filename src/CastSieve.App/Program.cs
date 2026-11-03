namespace CastSieve.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppPaths.MigrateFromOldName(); // 設定などを読む前に、旧名 (MSorter) のフォルダを引き継ぐ
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
