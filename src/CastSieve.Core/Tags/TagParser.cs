using System.Text.RegularExpressions;

namespace CastSieve.Tags;

/// <summary>プロンプト文字列をタグのリストに分解して正規化する。</summary>
public static class TagParser
{
    private static readonly Regex AngleRe = new(@"<[^>]*>", RegexOptions.Compiled);              // <lora:...>
    private static readonly Regex BreakRe = new(@"\bBREAK\b", RegexOptions.Compiled);
    private static readonly Regex NaiWeightRe = new(@"-?\d+(?:\.\d+)?::", RegexOptions.Compiled); // 1.2::tag::
    private static readonly Regex WeightSuffixRe = new(@":\s*-?\d+(?:\.\d+)?\s*(?=[)\]}]*\s*$)", RegexOptions.Compiled); // (tag:1.2)
    private static readonly Regex WhitespaceRe = new(@"\s+", RegexOptions.Compiled);

    private const string EscapedOpen = "\u0001";
    private const string EscapedClose = "\u0002";

    private static readonly (char Open, char Close)[] Pairs = { ('(', ')'), ('[', ']'), ('{', '}') };

    /// <summary>正規化済みタグを出現順・重複なしで返す(小文字、空白区切り)。</summary>
    public static List<string> Split(string prompt)
    {
        prompt = AngleRe.Replace(prompt, ",");
        prompt = prompt.Replace("\\(", EscapedOpen).Replace("\\)", EscapedClose);
        prompt = BreakRe.Replace(prompt, ",");
        prompt = prompt.Replace('\n', ',').Replace('\r', ',');

        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in prompt.Split(','))
        {
            string t = NaiWeightRe.Replace(raw, "");
            t = t.Replace("::", "");
            t = WeightSuffixRe.Replace(t, "");
            t = StripEmphasis(t);
            t = t.Replace(EscapedOpen, "(").Replace(EscapedClose, ")");
            t = NormalizeTag(t);

            if (t.Length > 0 && seen.Add(t))
                tags.Add(t);
        }
        return tags;
    }

    public static string NormalizeTag(string tag) =>
        WhitespaceRe.Replace(tag.Replace('_', ' '), " ").Trim().ToLowerInvariant();

    /// <summary>Danbooru のタグ名(小文字・アンダースコア区切り)へ変換する。</summary>
    public static string ToDanbooruName(string normalizedTag) => normalizedTag.Replace(' ', '_');

    // 強調用の ( ) [ ] { } を外す。"name (series)" のように名前の一部になっている括弧は残す。
    private static string StripEmphasis(string t)
    {
        bool changed = true;
        while (changed && t.Length > 0)
        {
            changed = false;
            t = t.Trim();
            foreach (var (open, close) in Pairs)
            {
                if (t.Length == 0) break;
                if (IsWrapped(t, open, close))
                {
                    t = t[1..^1];
                    changed = true;
                }
                else if (t[0] == open && Count(t, open) > Count(t, close))
                {
                    t = t[1..];
                    changed = true;
                }
                else if (t[^1] == close && Count(t, close) > Count(t, open))
                {
                    t = t[..^1];
                    changed = true;
                }
            }
        }
        return t.Trim();
    }

    private static bool IsWrapped(string t, char open, char close)
    {
        if (t.Length < 2 || t[0] != open || t[^1] != close) return false;
        int depth = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == open) depth++;
            else if (t[i] == close)
            {
                depth--;
                if (depth == 0 && i != t.Length - 1) return false;
            }
        }
        return depth == 0;
    }

    private static int Count(string s, char c)
    {
        int n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }
}
