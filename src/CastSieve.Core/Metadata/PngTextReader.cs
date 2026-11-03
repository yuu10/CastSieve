using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CastSieve.Metadata;

/// <summary>PNG の tEXt / zTXt / iTXt チャンクを (キーワード → テキスト) として読み取る。</summary>
public static class PngTextReader
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private const int MaxTextChunkBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <returns>PNG でなければ null。</returns>
    public static Dictionary<string, string>? Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var sig = new byte[8];
        if (!StreamUtil.TryFill(fs, sig, 8) || !sig.AsSpan().SequenceEqual(Signature))
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var header = new byte[8];

        while (StreamUtil.TryFill(fs, header, 8))
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.ASCII.GetString(header, 4, 4);
            if (type == "IEND") break;

            bool isText = type == "tEXt" || type == "zTXt" || type == "iTXt";
            if (isText && length <= MaxTextChunkBytes)
            {
                var data = new byte[length];
                if (!StreamUtil.TryFill(fs, data, (int)length)) break;
                fs.Seek(4, SeekOrigin.Current); // CRC
                ParseChunk(type, data, result);
            }
            else
            {
                fs.Seek((long)length + 4, SeekOrigin.Current); // データ + CRC を読み飛ばす
            }
        }

        return result;
    }

    private static void ParseChunk(string type, byte[] data, Dictionary<string, string> result)
    {
        int nul = Array.IndexOf(data, (byte)0);
        if (nul <= 0) return;

        string key = Encoding.Latin1.GetString(data, 0, nul);
        string? text = null;

        if (type == "tEXt")
        {
            text = DecodeText(data, nul + 1, data.Length - (nul + 1));
        }
        else if (type == "zTXt")
        {
            // keyword \0 compressionMethod(1) compressedData
            if (data.Length > nul + 2) text = Inflate(data, nul + 2);
        }
        else if (type == "iTXt")
        {
            // keyword \0 compressionFlag(1) compressionMethod(1) language \0 translatedKeyword \0 text
            if (data.Length < nul + 3) return;
            bool compressed = data[nul + 1] == 1;
            int langEnd = Array.IndexOf(data, (byte)0, nul + 3);
            if (langEnd < 0) return;
            int transEnd = Array.IndexOf(data, (byte)0, langEnd + 1);
            if (transEnd < 0) return;
            int start = transEnd + 1;
            text = compressed
                ? Inflate(data, start)
                : Encoding.UTF8.GetString(data, start, data.Length - start);
        }

        if (text != null && !result.ContainsKey(key))
            result[key] = text;
    }

    private static string? Inflate(byte[] data, int offset)
    {
        try
        {
            using var input = new MemoryStream(data, offset, data.Length - offset);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var bytes = output.ToArray();
            return DecodeText(bytes, 0, bytes.Length);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>tEXt は規格上 Latin-1 だが、実際には UTF-8 で書くツールが多いので UTF-8 を優先して試す。</summary>
    private static string DecodeText(byte[] bytes, int offset, int count)
    {
        try
        {
            return StrictUtf8.GetString(bytes, offset, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes, offset, count);
        }
    }
}
