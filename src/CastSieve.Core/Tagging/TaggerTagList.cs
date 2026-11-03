using System.Text;

namespace CastSieve.Tagging;

public sealed record TaggerTag(string Name, int Category);

/// <summary>
/// WD Tagger の selected_tags.csv (tag_id,name,category,count) を読む。
/// 行の順番がモデルの出力の並びと対応するので、読めない行も位置を保つために空のタグとして残す。
/// category: 0 = 一般, 4 = キャラクター, 9 = レーティング (Danbooru のカテゴリ番号と同じ)
/// </summary>
public static class TaggerTagList
{
    public const int CharacterCategory = 4;

    public static List<TaggerTag> Parse(IEnumerable<string> lines)
    {
        var tags = new List<TaggerTag>();
        bool first = true;

        foreach (string line in lines)
        {
            if (line.Length == 0) continue;
            var fields = SplitCsvLine(line);

            if (first)
            {
                first = false;
                if (fields.Contains("name")) continue; // ヘッダー行
            }

            if (fields.Count >= 3 && int.TryParse(fields[2], out int category))
                tags.Add(new TaggerTag(fields[1], category));
            else
                tags.Add(new TaggerTag("", -1));
        }
        return tags;
    }

    public static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
