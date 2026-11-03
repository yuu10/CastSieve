using System.Text.Json;
using System.Text.Json.Nodes;

namespace CastSieve.Metadata;

public sealed record PromptInfo(string Prompt, string Source)
{
    public const string None = "none";

    public bool HasMetadata => Source != None && !Source.StartsWith("error", StringComparison.Ordinal);
}

/// <summary>画像ファイルから「ポジティブプロンプト」を取り出す。</summary>
public static class PromptExtractor
{
    public static PromptInfo Extract(string path)
    {
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        string? exifComment = null;

        try
        {
            if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                var png = PngTextReader.Read(path);
                if (png != null) info = png;
            }
            else
            {
                var exif = ExifTextReader.Read(path);
                if (exif != null)
                {
                    exifComment = exif.UserComment;
                    // ComfyUI の WebP/JPEG は Exif の文字列タグに "prompt:{...}" を入れる
                    foreach (var s in exif.Ifd0Strings)
                    {
                        if (s.StartsWith("prompt:", StringComparison.OrdinalIgnoreCase))
                            info.TryAdd("prompt", s[7..]);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new PromptInfo("", "error: " + ex.Message);
        }

        return FromInfo(info, exifComment);
    }

    /// <summary>読み取り済みのメタデータからプロンプトを組み立てる(テストしやすいよう公開)。</summary>
    public static PromptInfo FromInfo(IReadOnlyDictionary<string, string> info, string? exifComment)
    {
        // AUTOMATIC1111 / Forge
        if (info.TryGetValue("parameters", out var parameters) && !string.IsNullOrWhiteSpace(parameters))
        {
            if (parameters.TrimStart().StartsWith('{'))
            {
                var prompt = Str(TryParseObject(parameters)?["prompt"]);
                if (prompt != null) return new PromptInfo(prompt, "json-parameters");
            }
            return new PromptInfo(A1111Positive(parameters), "a1111");
        }

        // NovelAI
        var nai = NovelAiPrompt(info);
        if (!string.IsNullOrWhiteSpace(nai)) return new PromptInfo(nai, "novelai");

        // ComfyUI
        if (info.TryGetValue("prompt", out var comfyRaw))
        {
            var nodes = TryParseObject(comfyRaw);
            if (nodes != null)
            {
                var text = ComfyPositive(nodes);
                if (!string.IsNullOrWhiteSpace(text)) return new PromptInfo(text, "comfyui");
            }
        }

        // InvokeAI
        if (info.TryGetValue("invokeai_metadata", out var invokeRaw))
        {
            var pos = Str(TryParseObject(invokeRaw)?["positive_prompt"]);
            if (pos != null) return new PromptInfo(pos, "invokeai");
        }

        // JPEG / WebP の Exif UserComment (A1111 形式)
        if (!string.IsNullOrWhiteSpace(exifComment))
            return new PromptInfo(A1111Positive(exifComment), "exif");

        return new PromptInfo("", PromptInfo.None);
    }

    // ---- A1111 ------------------------------------------------------------

    private static string A1111Positive(string text)
    {
        int i = text.IndexOf("\nNegative prompt:", StringComparison.Ordinal);
        if (i < 0) i = text.IndexOf("\nSteps: ", StringComparison.Ordinal);
        return i >= 0 ? text[..i] : text;
    }

    // ---- NovelAI ----------------------------------------------------------

    private static string? NovelAiPrompt(IReadOnlyDictionary<string, string> info)
    {
        if (info.TryGetValue("Comment", out var comment))
        {
            var d = TryParseObject(comment);
            if (d != null && (d.ContainsKey("prompt") || d.ContainsKey("v4_prompt")))
            {
                var parts = new List<string>();
                var p = Str(d["prompt"]);
                if (p != null) parts.Add(p);

                // V4: ベースプロンプトとキャラクター別プロンプトが分かれている
                if (d["v4_prompt"] is JsonObject v4 && v4["caption"] is JsonObject cap)
                {
                    var b = Str(cap["base_caption"]);
                    if (b != null) parts.Add(b);
                    if (cap["char_captions"] is JsonArray arr)
                    {
                        foreach (var c in arr)
                        {
                            var cc = c is JsonObject co ? Str(co["char_caption"]) : null;
                            if (cc != null) parts.Add(cc);
                        }
                    }
                }
                return string.Join(", ", parts);
            }
        }

        if (info.TryGetValue("Software", out var sw) && sw == "NovelAI"
            && info.TryGetValue("Description", out var desc))
            return desc;

        return null;
    }

    // ---- ComfyUI ----------------------------------------------------------

    private static readonly string[] ComfyTextKeys = { "text", "text_g", "text_l", "prompt", "positive", "string", "value" };
    private static readonly string[] ComfySkipKeys = { "clip", "model", "vae", "negative", "latent_image", "image", "samples" };

    private static string? ComfyPositive(JsonObject nodes)
    {
        var texts = new List<string>();

        foreach (var kv in nodes)
        {
            if (kv.Value is not JsonObject node) continue;
            string cls = Str(node["class_type"]) ?? "";
            if (cls.Contains("KSampler", StringComparison.Ordinal)
                && node["inputs"] is JsonObject inputs
                && inputs["positive"] is JsonArray link && link.Count == 2)
            {
                CollectComfyTexts(nodes, link[0]?.ToString(), texts, 0);
            }
        }

        if (texts.Count == 0)
        {
            // KSampler が見つからない場合は全 CLIPTextEncode を対象にする
            foreach (var kv in nodes)
            {
                if (kv.Value is not JsonObject node) continue;
                string cls = Str(node["class_type"]) ?? "";
                if (!cls.StartsWith("CLIPTextEncode", StringComparison.Ordinal)) continue;
                if (node["inputs"] is not JsonObject inputs) continue;
                foreach (var input in inputs)
                {
                    var s = Str(input.Value);
                    if (s != null && ComfyTextKeys.Contains(input.Key)) texts.Add(s);
                }
            }
        }

        var unique = texts.Distinct().ToList();
        return unique.Count == 0 ? null : string.Join(", ", unique);
    }

    private static void CollectComfyTexts(JsonObject nodes, string? id, List<string> texts, int depth)
    {
        if (id == null || depth > 8) return;
        if (!nodes.TryGetPropertyValue(id, out var raw) || raw is not JsonObject node) return;
        if (node["inputs"] is not JsonObject inputs) return;

        foreach (var input in inputs)
        {
            if (ComfySkipKeys.Contains(input.Key)) continue;

            var s = Str(input.Value);
            if (s != null)
            {
                if (ComfyTextKeys.Contains(input.Key)) texts.Add(s);
            }
            else if (input.Value is JsonArray link && link.Count == 2)
            {
                CollectComfyTexts(nodes, link[0]?.ToString(), texts, depth + 1);
            }
        }
    }

    // ---- JSON helpers -----------------------------------------------------

    private static JsonObject? TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
