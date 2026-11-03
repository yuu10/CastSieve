using System.Text;

namespace CastSieve.Sorting;

public static class FolderNames
{
    public const string Unknown = "_unknown";          // メタデータはあるがキャラクタータグなし
    public const string NoMetadata = "_no_metadata";   // メタデータなし/読み取り不可
    public const string Multiple = "_multiple";        // 複数キャラクター

    private static readonly char[] InvalidChars = "<>:\"/\\|?*".ToCharArray();

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Windows でも使えるフォルダ名にする。</summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsControl(c) || Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c);

        string s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length > 100) s = s[..100].TrimEnd('.', ' ');
        if (s.Length == 0) return "_unnamed";
        if (Reserved.Contains(s)) s = "_" + s;
        return s;
    }
}
