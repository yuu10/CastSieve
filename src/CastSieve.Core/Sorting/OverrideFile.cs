using System.Text;
using CastSieve.Tags;

namespace CastSieve.Sorting;

/// <summary>
/// 「タグ,フォルダ名」形式の上書きリストを読む。Danbooru の判定より優先される。
/// 例) hatsune miku,初音ミク
///     # から始まる行はコメント
/// </summary>
public static class OverrideFile
{
    public static Dictionary<string, string> Load(string? path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return map;

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int comma = line.IndexOf(',');
            string tag = TagParser.NormalizeTag(comma < 0 ? line : line[..comma]);
            string folder = comma < 0 ? tag : line[(comma + 1)..].Trim();
            if (tag.Length > 0 && folder.Length > 0)
                map[tag] = folder;
        }
        return map;
    }
}
