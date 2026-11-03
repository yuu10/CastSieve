using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using CastSieve.Imaging;
using CastSieve.Tags;
using SkiaSharp;

namespace CastSieve.Tagging;

/// <param name="Tag">正規化済みのタグ名 (小文字・空白区切り)。</param>
/// <param name="Score">モデルの確信度 (0〜1)。</param>
public sealed record TaggerPrediction(string Tag, float Score);

public interface IImageTagger : IDisposable
{
    /// <summary>
    /// 画像に写っているキャラクターを、確信度の高い順に返す。しきい値では絞らない
    /// (しきい値未満のものは、手で仕分け先を選ぶときの候補になる)。
    /// </summary>
    IReadOnlyList<TaggerPrediction> PredictCharacters(string imagePath);

    /// <summary>推定に使っている計算装置の説明 (例: "GPU (DirectML)")。</summary>
    string? Description => null;

    /// <summary>GPU から CPU への切り替えなど、利用者に知らせたいこと。</summary>
    string? Warning => null;
}

/// <summary>
/// WD Tagger (SmilingWolf) v3 系の ONNX モデルで、画像から Danbooru 形式のタグを推定する。
/// 前処理: 透明部分を白で塗る → 白で正方形に余白を足す → 入力サイズへ縮小 → BGR・0〜255 の float (NHWC)。
/// 出力は各タグの確率 (シグモイド済み)。
/// </summary>
public sealed class WdTagger : IImageTagger
{
    /// <summary>これ未満の確信度のキャラクターは、候補としても返さない。</summary>
    public const double CandidateFloor = 0.1;

    private readonly string _modelPath;
    private readonly List<TaggerTag> _tags;
    private InferenceSession _session;
    private string _inputName;
    private int _size;
    private bool _onGpu;

    public string? Description { get; private set; }
    public string? Warning { get; private set; }

    /// <param name="useGpu">true なら GPU (DirectML) を試し、使えなければ CPU に切り替える。</param>
    public WdTagger(string modelDir, bool useGpu = false)
    {
        _tags = TaggerTagList.Parse(File.ReadLines(Path.Combine(modelDir, ModelCatalog.TagsFileName), Encoding.UTF8));
        _modelPath = Path.Combine(modelDir, ModelCatalog.ModelFileName);

        InferenceSession? session = null;
        if (useGpu)
        {
            try
            {
                session = CreateGpuSession(_modelPath);
                _onGpu = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // GPU・ドライバ・ライブラリの問題は種類が多いので、境界でまとめて CPU に切り替える
                Warning = $"GPUを使えなかったため、CPUで推定します ({ex.Message})";
            }
        }

        _session = session ?? new InferenceSession(_modelPath);
        Description = _onGpu ? "GPU (DirectML)" : "CPU";
        (_inputName, _size) = ReadInput(_session);
    }

    private static InferenceSession CreateGpuSession(string modelPath)
    {
        using var options = new SessionOptions();
        options.AppendExecutionProvider_DML(0);
        // DirectML の推奨設定
        options.EnableMemoryPattern = false;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        return new InferenceSession(modelPath, options);
    }

    private static (string Name, int Size) ReadInput(InferenceSession session)
    {
        string name = session.InputMetadata.Keys.First();
        var dims = session.InputMetadata[name].Dimensions; // 例: [-1, 448, 448, 3]
        return (name, dims.Length == 4 && dims[1] > 3 ? dims[1] : 448);
    }

    public IReadOnlyList<TaggerPrediction> PredictCharacters(string imagePath)
    {
        var input = Preprocess(imagePath);

        float[] scores;
        try
        {
            scores = Run(input);
        }
        catch (OnnxRuntimeException ex) when (_onGpu)
        {
            // 読み込めても、実行時に GPU で失敗することがある。CPU に切り替えてやり直す
            SwitchToCpu(ex.Message);
            scores = Run(input);
        }

        if (scores.Length != _tags.Count)
            throw new InvalidDataException(
                $"モデルの出力数 ({scores.Length}) とタグ一覧の行数 ({_tags.Count}) が一致しません。別のモデルのファイルが混ざっていないか確認してください。");

        return SelectCharacters(scores, _tags, CandidateFloor);
    }

    private float[] Run(DenseTensor<float> input)
    {
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) });
        return results.First().AsEnumerable<float>().ToArray();
    }

    private void SwitchToCpu(string reason)
    {
        _session.Dispose();
        _session = new InferenceSession(_modelPath);
        (_inputName, _size) = ReadInput(_session);
        _onGpu = false;
        Description = "CPU";
        Warning = $"GPUでの推論に失敗したため、CPUに切り替えました ({reason})";
    }

    /// <summary>しきい値以上のキャラクタータグを、確信度の高い順に返す。</summary>
    public static List<TaggerPrediction> SelectCharacters(
        float[] scores, IReadOnlyList<TaggerTag> tags, double threshold)
    {
        var picked = new List<TaggerPrediction>();
        int count = Math.Min(scores.Length, tags.Count);
        for (int i = 0; i < count; i++)
        {
            if (tags[i].Category == TaggerTagList.CharacterCategory && scores[i] >= threshold)
                picked.Add(new TaggerPrediction(TagParser.NormalizeTag(tags[i].Name), scores[i]));
        }
        picked.Sort((a, b) => b.Score.CompareTo(a.Score));
        return picked;
    }

    private DenseTensor<float> Preprocess(string path)
    {
        using var image = SkiaImage.TryLoad(path)
            ?? throw new InvalidDataException($"画像を読み込めませんでした: {path}");

        // 白で正方形に余白を足してから入力サイズへ縮小するのと同じになるよう、中央に縮小して描く
        int size = _size;
        float scale = (float)size / Math.Max(image.Width, image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        var dest = SKRect.Create((size - w) / 2, (size - h) / 2, w, h);
        using var canvas = SkiaImage.RenderOnWhite(image, size, size, dest, cubic: true);

        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        var buffer = tensor.Buffer.Span;
        var pixels = canvas.GetPixelSpan();
        int rowBytes = canvas.RowBytes;
        for (int y = 0; y < size; y++)
        {
            var row = pixels.Slice(y * rowBytes, size * 4);
            for (int x = 0; x < size; x++)
            {
                int offset = (y * size + x) * 3;
                buffer[offset] = row[x * 4 + 2];     // B
                buffer[offset + 1] = row[x * 4 + 1]; // G
                buffer[offset + 2] = row[x * 4];     // R
            }
        }
        return tensor;
    }

    public void Dispose() => _session.Dispose();
}
