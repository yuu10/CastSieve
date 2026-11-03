using SkiaSharp;

namespace CastSieve.Imaging;

/// <summary>SkiaSharp での画像の読み込みと描画の共通処理。</summary>
internal static class SkiaImage
{
    /// <summary>画像を読み込み、EXIF の向きを反映して返す。読み込めなかった場合は null。</summary>
    public static SKBitmap? TryLoad(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bitmap;
        using (bitmap)
            return Orient(bitmap, codec.EncodedOrigin);
    }

    /// <summary>EXIF の向き (1〜8) のとおりに回転・反転した画像を作る。</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        bool swap = origin >= SKEncodedOrigin.LeftTop;
        // x' = a*x + b*y + c, y' = d*x + e*y + f
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),     // 左右反転
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),  // 180 度
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),   // 上下反転
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),      // 転置
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),     // 右に 90 度
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),  // 転置 + 180 度
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),   // 左に 90 度
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        var info = source.Info.WithSize(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        var rotated = new SKBitmap(info);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(new SKMatrix(a, b, c, d, e, f, 0, 0, 1));
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return rotated;
    }

    /// <summary>
    /// 白で塗った width×height の画像に、source を dest の位置・大きさで描く。
    /// 縮小率が大きいとキュービック補間だけではギザつくため、先にミップマップ付きの線形補間で縮めておく。
    /// </summary>
    public static SKBitmap RenderOnWhite(SKBitmap source, int width, int height, SKRect dest, bool cubic)
    {
        SKBitmap? shrunk = null;
        if (cubic && (source.Width > dest.Width * 2 || source.Height > dest.Height * 2))
        {
            var mid = source.Info.WithSize(
                Math.Max(1, (int)Math.Round(dest.Width * 2)), Math.Max(1, (int)Math.Round(dest.Height * 2)));
            shrunk = source.Resize(mid, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        try
        {
            var sampling = cubic
                ? new SKSamplingOptions(SKCubicResampler.CatmullRom)
                : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

            var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(shrunk ?? source);
            canvas.DrawImage(image, dest, sampling);
            canvas.Flush();
            return output;
        }
        finally
        {
            shrunk?.Dispose();
        }
    }
}
