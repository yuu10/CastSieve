using System.Buffers.Binary;
using SkiaSharp;

namespace CastSieve.Imaging;

/// <summary>
/// 画像の縮小表示 (サムネイル) をメモリ上だけで作る。ディスクには何も書き込まない。
/// 結果は BMP 形式のバイト列で返す (エンコードもデコードも軽いため)。
/// </summary>
public static class ThumbnailMaker
{
    /// <summary>
    /// 縦横が maxSize に収まるよう縮小した画像を返す。小さい画像は拡大しない。
    /// 透明な部分は白で塗る。読み込めなかった場合は null。
    /// </summary>
    public static byte[]? TryCreateBmp(string path, int maxSize)
    {
        try
        {
            using var image = SkiaImage.TryLoad(path);
            if (image == null) return null;

            int width = image.Width, height = image.Height;
            if (width > maxSize || height > maxSize)
            {
                double scale = Math.Min((double)maxSize / width, (double)maxSize / height);
                width = Math.Max(1, (int)Math.Round(width * scale));
                height = Math.Max(1, (int)Math.Round(height * scale));
            }

            using var flat = SkiaImage.RenderOnWhite(image, width, height, new SKRect(0, 0, width, height), cubic: false);
            return EncodeBmp24(flat);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // デコーダーが投げる例外の種類が多いため、ここでまとめて「作れなかった」として扱う
            return null;
        }
    }

    /// <summary>不透明な RGBA の画像を 24 ビットの BMP にする (SkiaSharp は BMP を書き出せないため)。</summary>
    private static byte[] EncodeBmp24(SKBitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        int stride = (width * 3 + 3) & ~3;
        const int headerSize = 54;
        var bytes = new byte[headerSize + stride * height];
        var span = bytes.AsSpan();

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], height); // 正の値 = 下の行から
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(span[34..], stride * height);

        var pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < height; y++)
        {
            var src = pixels.Slice(y * rowBytes, width * 4);
            var dst = span.Slice(headerSize + (height - 1 - y) * stride, width * 3);
            for (int x = 0; x < width; x++)
            {
                dst[x * 3] = src[x * 4 + 2];     // B
                dst[x * 3 + 1] = src[x * 4 + 1]; // G
                dst[x * 3 + 2] = src[x * 4];     // R
            }
        }
        return bytes;
    }
}
