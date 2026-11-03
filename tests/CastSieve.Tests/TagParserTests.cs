using CastSieve.Tags;

namespace CastSieve.Tests;

public class TagParserTests
{
    [Fact]
    public void Removes_weights_emphasis_lora_and_keeps_series_parentheses()
    {
        var tags = TagParser.Split(
            "1girl, (hatsune miku \\(vocaloid\\):1.2), <lora:x:0.8>, [[solo]], 1.2::smile::, rem_(re:zero)");

        Assert.Equal(
            new[] { "1girl", "hatsune miku (vocaloid)", "solo", "smile", "rem (re:zero)" },
            tags);
    }

    [Fact]
    public void Handles_nested_emphasis_around_series_tag()
    {
        var tags = TagParser.Split("((ganyu (genshin impact))), {{{frieren}}}");
        Assert.Equal(new[] { "ganyu (genshin impact)", "frieren" }, tags);
    }

    [Fact]
    public void Removes_duplicates_and_normalizes_case_and_underscores()
    {
        var tags = TagParser.Split("Long_Hair, long hair, LONG HAIR");
        Assert.Equal(new[] { "long hair" }, tags);
    }

    [Fact]
    public void Treats_BREAK_and_newlines_as_separators()
    {
        var tags = TagParser.Split("1girl BREAK solo\nsmile");
        Assert.Equal(new[] { "1girl", "solo", "smile" }, tags);
    }

    [Fact]
    public void Converts_to_danbooru_name()
    {
        Assert.Equal("rem_(re:zero)", TagParser.ToDanbooruName("rem (re:zero)"));
    }
}
