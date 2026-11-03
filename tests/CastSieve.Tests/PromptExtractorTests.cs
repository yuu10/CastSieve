using System.Buffers.Binary;
using System.Text;
using CastSieve.Metadata;

namespace CastSieve.Tests;

public class PromptExtractorTests
{
    private static PromptInfo FromInfo(params (string Key, string Value)[] entries) =>
        PromptExtractor.FromInfo(entries.ToDictionary(e => e.Key, e => e.Value), null);

    [Fact]
    public void A1111_returns_only_the_positive_prompt()
    {
        var info = FromInfo(("parameters", "1girl, solo\nNegative prompt: bad hands\nSteps: 20, Sampler: Euler"));
        Assert.Equal("a1111", info.Source);
        Assert.Equal("1girl, solo", info.Prompt);
    }

    [Fact]
    public void NovelAi_V4_includes_character_captions()
    {
        const string comment = """
            {"prompt":"1girl, base","v4_prompt":{"caption":{"base_caption":"1girl, base","char_captions":[{"char_caption":"rem (re:zero)"},{"char_caption":"ram (re:zero)"}]}}}
            """;
        var info = FromInfo(("Comment", comment.Trim()));
        Assert.Equal("novelai", info.Source);
        Assert.Contains("rem (re:zero)", info.Prompt);
        Assert.Contains("ram (re:zero)", info.Prompt);
    }

    [Fact]
    public void ComfyUi_follows_the_positive_link_and_ignores_negative()
    {
        const string workflow = """
            {
              "1": {"class_type": "CLIPTextEncode", "inputs": {"text": "1girl, hakurei reimu (touhou)", "clip": ["9", 1]}},
              "2": {"class_type": "CLIPTextEncode", "inputs": {"text": "lowres, bad anatomy", "clip": ["9", 1]}},
              "3": {"class_type": "KSampler", "inputs": {"positive": ["1", 0], "negative": ["2", 0], "model": ["9", 0]}}
            }
            """;
        var info = FromInfo(("prompt", workflow));
        Assert.Equal("comfyui", info.Source);
        Assert.Equal("1girl, hakurei reimu (touhou)", info.Prompt);
    }

    [Fact]
    public void Returns_none_when_there_is_no_metadata()
    {
        var info = FromInfo();
        Assert.Equal(PromptInfo.None, info.Source);
        Assert.False(info.HasMetadata);
    }

    [Fact]
    public void Reads_text_chunks_from_a_png_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cs_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, FakePng(("parameters", "1girl, 日本語タグ\nNegative prompt: x\nSteps: 1")));
            var info = PromptExtractor.Extract(path);
            Assert.Equal("a1111", info.Source);
            Assert.Equal("1girl, 日本語タグ", info.Prompt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>テスト用: tEXt チャンクだけを持つ最小限の PNG (CRC は検証しないので 0 埋め)。</summary>
    internal static byte[] FakePng(params (string Key, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        foreach (var (key, text) in entries)
        {
            var data = Encoding.UTF8.GetBytes(key + "\0" + text);
            var len = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            ms.Write(len);
            ms.Write(Encoding.ASCII.GetBytes("tEXt"));
            ms.Write(data);
            ms.Write(new byte[4]);
        }
        ms.Write(new byte[4]);
        ms.Write(Encoding.ASCII.GetBytes("IEND"));
        ms.Write(new byte[4]);
        return ms.ToArray();
    }
}
