using CastSieve.Imaging;
using SkiaSharp;

namespace CastSieve.Tests;

public class ThumbnailTests
{
    private static string TempPng(int width, int height, SKColor color)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms_thumb_{Guid.NewGuid():N}.png");
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void Shrinks_to_fit_and_keeps_the_aspect_ratio()
    {
        string path = TempPng(400, 200, new SKColor(10, 20, 30));
        try
        {
            var bytes = ThumbnailMaker.TryCreateBmp(path, 100);

            Assert.NotNull(bytes);
            using var thumb = SKBitmap.Decode(bytes!);
            Assert.Equal(100, thumb.Width);
            Assert.Equal(50, thumb.Height);
            Assert.Equal(new SKColor(10, 20, 30), thumb.GetPixel(50, 25));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Does_not_enlarge_small_images()
    {
        string path = TempPng(50, 30, new SKColor(10, 20, 30));
        try
        {
            var bytes = ThumbnailMaker.TryCreateBmp(path, 120);

            using var thumb = SKBitmap.Decode(bytes!);
            Assert.Equal(50, thumb.Width);
            Assert.Equal(30, thumb.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fills_transparent_areas_with_white()
    {
        string path = TempPng(20, 20, new SKColor(0, 0, 0, 0));
        try
        {
            var bytes = ThumbnailMaker.TryCreateBmp(path, 20);

            using var thumb = SKBitmap.Decode(bytes!);
            Assert.Equal(SKColors.White, thumb.GetPixel(0, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Applies_the_exif_orientation()
    {
        // 左半分が赤・右半分が青の横長 JPEG に「右に 90 度回して表示」(向き 6) を付ける → 縦長で上が赤
        using var bitmap = new SKBitmap(40, 20);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 20, 20, red);
        }
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        byte[] exif =
        {
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0x00, 0x06, 0, 0,
            0, 0, 0, 0,
        };
        byte[] original = jpeg.ToArray();
        string path = Path.Combine(Path.GetTempPath(), $"ms_thumb_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, original[..2].Concat(exif).Concat(original[2..]).ToArray());
        try
        {
            var bytes = ThumbnailMaker.TryCreateBmp(path, 100);

            using var thumb = SKBitmap.Decode(bytes!);
            Assert.Equal(20, thumb.Width);
            Assert.Equal(40, thumb.Height);
            var top = thumb.GetPixel(10, 5);
            var bottom = thumb.GetPixel(10, 35);
            Assert.True(top.Red > 200 && top.Blue < 60, $"上 = {top}");
            Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"下 = {bottom}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Returns_null_for_files_that_are_not_images_or_missing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms_thumb_{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "this is not an image");
        try
        {
            Assert.Null(ThumbnailMaker.TryCreateBmp(path, 100));
            Assert.Null(ThumbnailMaker.TryCreateBmp(path + ".missing", 100));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_evicts_the_least_recently_used_and_reports_removals()
    {
        var removed = new List<string>();
        using var cache = new LruCache<string, string>(2, removed.Add);

        cache.Set("a", "A");
        cache.Set("b", "B");
        Assert.True(cache.TryGet("a", out _)); // a を使ったので、捨てられるのは b
        cache.Set("c", "C");

        Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("a"));
        Assert.True(cache.Contains("c"));
        Assert.Equal(new[] { "B" }, removed);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Cache_replaces_existing_keys_and_clears()
    {
        var removed = new List<string>();
        var cache = new LruCache<string, string>(3, removed.Add);

        cache.Set("a", "old");
        cache.Set("a", "new");
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal("new", value);
        Assert.Equal(new[] { "old" }, removed);

        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(new[] { "old", "new" }, removed);
    }
}
