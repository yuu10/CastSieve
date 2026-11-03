using System.Text.Json;

namespace CastSieve.App;

/// <summary>画面の表示設定。サムネイルの画像そのものは保存せず、オン/オフと大きさだけを保存する。</summary>
internal sealed class AppSettings
{
    public static readonly int[] ThumbnailSizes = { 48, 80, 120 };

    public bool ShowThumbnails { get; set; } = true;
    public int ThumbnailSize { get; set; } = 80;

    /// <summary>結果の一覧を、仕分け先のフォルダごとにまとめて表示する。</summary>
    public bool GroupByFolder { get; set; } = true;

    /// <summary>画面のテーマ(システムに合わせる / ライト / ダーク)。</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    private static string FilePath =>
        Path.Combine(AppPaths.DataRoot, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings != null)
                {
                    if (!ThumbnailSizes.Contains(settings.ThumbnailSize)) settings.ThumbnailSize = 80;
                    if (!Enum.IsDefined(settings.Theme)) settings.Theme = ThemeMode.System;
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 読めない設定ファイルは無視して既定値で始める
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくても、そのまま使い続けられる
        }
    }
}
