namespace CastSieve.Metadata;

internal static class StreamUtil
{
    /// <summary>buffer の先頭 count バイトを読み切る。ストリーム終端に達したら false。</summary>
    public static bool TryFill(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }
}
