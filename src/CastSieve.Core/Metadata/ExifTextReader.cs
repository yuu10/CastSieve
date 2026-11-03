using System.Buffers.Binary;
using System.Text;

namespace CastSieve.Metadata;

/// <param name="UserComment">Exif IFD の UserComment (A1111 が JPEG/WebP に書く生成パラメータ)。</param>
/// <param name="Ifd0Strings">IFD0 の ASCII 文字列タグ (ComfyUI は "prompt:{json}" をここに入れる)。</param>
public sealed record ExifInfo(string? UserComment, IReadOnlyList<string> Ifd0Strings);

/// <summary>JPEG / WebP から Exif を取り出して必要な文字列だけ読む軽量リーダー。</summary>
public static class ExifTextReader
{
    private const int MaxExifBytes = 16 * 1024 * 1024;

    public static ExifInfo? Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[]? tiff = ExtractTiff(fs);
        return tiff == null ? null : ParseTiff(tiff);
    }

    private static byte[]? ExtractTiff(FileStream fs)
    {
        var head = new byte[12];
        int n = fs.Read(head, 0, head.Length);
        fs.Seek(0, SeekOrigin.Begin);

        if (n >= 12
            && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
            && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
            return ExtractFromWebp(fs);

        if (n >= 2 && head[0] == 0xFF && head[1] == 0xD8)
            return ExtractFromJpeg(fs);

        return null;
    }

    private static byte[]? ExtractFromWebp(Stream fs)
    {
        fs.Seek(12, SeekOrigin.Begin);
        var h = new byte[8];
        while (StreamUtil.TryFill(fs, h, 8))
        {
            string id = Encoding.ASCII.GetString(h, 0, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4, 4));
            long padded = (long)size + (size & 1);

            if (id == "EXIF" && size <= MaxExifBytes)
            {
                var data = new byte[size];
                if (!StreamUtil.TryFill(fs, data, (int)size)) return null;
                return StripExifPrefix(data);
            }
            fs.Seek(padded, SeekOrigin.Current);
        }
        return null;
    }

    private static byte[]? ExtractFromJpeg(Stream fs)
    {
        fs.Seek(2, SeekOrigin.Begin); // SOI を飛ばす
        while (true)
        {
            int b = fs.ReadByte();
            if (b < 0) return null;
            if (b != 0xFF) continue;

            int marker;
            do { marker = fs.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker == 0xDA || marker == 0xD9) return null; // SOS / EOI 以降に Exif はない
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue; // 長さを持たないマーカー

            int hi = fs.ReadByte();
            int lo = fs.ReadByte();
            if (lo < 0) return null;
            int len = ((hi << 8) | lo) - 2;
            if (len < 0) return null;

            if (marker == 0xE1 && len >= 6 && len <= MaxExifBytes)
            {
                var seg = new byte[len];
                if (!StreamUtil.TryFill(fs, seg, len)) return null;
                if (seg[0] == 'E' && seg[1] == 'x' && seg[2] == 'i' && seg[3] == 'f')
                    return seg[6..];
            }
            else
            {
                fs.Seek(len, SeekOrigin.Current);
            }
        }
    }

    private static byte[] StripExifPrefix(byte[] d) =>
        d.Length > 6 && d[0] == 'E' && d[1] == 'x' && d[2] == 'i' && d[3] == 'f' ? d[6..] : d;

    private static ExifInfo? ParseTiff(byte[] t)
    {
        if (t.Length < 8) return null;

        bool le;
        if (t[0] == 'I' && t[1] == 'I') le = true;
        else if (t[0] == 'M' && t[1] == 'M') le = false;
        else return null;

        ushort U16(int o) => le
            ? BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(o, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(t.AsSpan(o, 2));
        uint U32(int o) => le
            ? BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(o, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(t.AsSpan(o, 4));

        string? userComment = null;
        var strings = new List<string>();

        void ReadIfd(uint offset, bool isIfd0)
        {
            if (offset == 0 || offset + 2 > t.Length) return;
            int pos = (int)offset;
            int count = U16(pos);
            pos += 2;

            for (int i = 0; i < count; i++, pos += 12)
            {
                if (pos + 12 > t.Length) return;
                ushort tag = U16(pos);
                ushort type = U16(pos + 2);
                uint cnt = U32(pos + 4);

                if (isIfd0 && tag == 0x8769 && type == 4) // Exif IFD へのポインタ
                {
                    ReadIfd(U32(pos + 8), false);
                    continue;
                }

                if (type != 2 && type != 7) continue; // ASCII / UNDEFINED のみ (1 バイト単位)
                long size = cnt;
                if (size <= 0 || size > t.Length) continue;
                long dataPos = size <= 4 ? pos + 8 : U32(pos + 8);
                if (dataPos + size > t.Length) continue;

                var bytes = t.AsSpan((int)dataPos, (int)size).ToArray();
                if (isIfd0 && type == 2)
                    strings.Add(Encoding.UTF8.GetString(bytes).TrimEnd('\0'));
                else if (!isIfd0 && tag == 0x9286)
                    userComment = DecodeUserComment(bytes);
            }
        }

        try
        {
            ReadIfd(U32(4), true);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 壊れた Exif: ここまでに読めた分だけ返す
        }

        return new ExifInfo(userComment, strings);
    }

    private static string? DecodeUserComment(byte[] b)
    {
        if (b.Length < 8) return null;
        string prefix = Encoding.ASCII.GetString(b, 0, 8);
        var body = b.AsSpan(8).ToArray();

        if (prefix.StartsWith("UNICODE", StringComparison.Ordinal))
        {
            // 仕様上は TIFF のバイトオーダーだが、A1111 (piexif) は常にビッグエンディアン。
            // ASCII 文字の比率が高いほうを正とする。
            string be = Encoding.BigEndianUnicode.GetString(body).TrimEnd('\0');
            string le = Encoding.Unicode.GetString(body).TrimEnd('\0');
            return AsciiRatio(be) >= AsciiRatio(le) ? be : le;
        }
        if (prefix.StartsWith("ASCII", StringComparison.Ordinal))
            return Encoding.ASCII.GetString(body).TrimEnd('\0');

        return Encoding.UTF8.GetString(body).TrimEnd('\0');
    }

    private static double AsciiRatio(string s) =>
        s.Length == 0 ? 0 : (double)s.Count(c => c < 0x80) / s.Length;
}
