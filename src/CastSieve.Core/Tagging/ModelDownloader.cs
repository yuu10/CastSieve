namespace CastSieve.Tagging;

public sealed record DownloadProgress(string FileName, long BytesDone, long? BytesTotal);

/// <summary>モデルを Hugging Face から保存先フォルダへダウンロードする。</summary>
public static class ModelDownloader
{
    public static async Task DownloadAsync(
        TaggerModel model, string? root, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        string dir = ModelCatalog.DirectoryFor(model, root);
        Directory.CreateDirectory(dir);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // 大きいファイルなので全体のタイムアウトは設けない
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CastSieve/1.0 (model download)");

        // 小さいタグ一覧を先に取得して、URL や通信の問題を早く検出する
        foreach (string file in new[] { ModelCatalog.TagsFileName, ModelCatalog.ModelFileName })
        {
            string dest = Path.Combine(dir, file);
            if (File.Exists(dest)) continue;
            await DownloadFileAsync(http, ModelCatalog.DownloadUrl(model, file), dest, file, progress, ct);
        }
    }

    private static async Task DownloadFileAsync(
        HttpClient http, string url, string dest, string fileName, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        string temp = dest + ".part";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            long done = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(
                             temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(new DownloadProgress(fileName, done, total));
                }
            }

            if (total.HasValue && done != total.Value)
                throw new IOException($"{fileName} のダウンロードが途中で終了しました ({done}/{total} バイト)");

            File.Move(temp, dest, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (IOException)
            {
                // 一時ファイルの削除に失敗しても次回の上書きで解消される
            }
        }
    }
}
