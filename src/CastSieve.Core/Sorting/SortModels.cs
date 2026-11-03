using CastSieve.Metadata;
using CastSieve.Tagging;

namespace CastSieve.Sorting;

public enum SortAction
{
    /// <summary>ファイルを変更せず、結果だけ確認する。</summary>
    Preview,
    Copy,
    Move,
}

public enum MultiMode
{
    /// <summary>複数キャラクターが検出された画像は _multiple フォルダへ。</summary>
    MultipleFolder,

    /// <summary>プロンプトで最初に出てくるキャラクターだけを採用する。</summary>
    FirstOnly,

    /// <summary>検出されたキャラクターすべてのフォルダへコピーする(移動の場合は最後の1つへ移動)。</summary>
    EachFolder,
}

public sealed class SortOptions
{
    public required string InputDir { get; init; }
    public required string OutputDir { get; init; }
    public bool Recursive { get; init; }

    /// <summary><see cref="SortService.RunAsync"/> で使う。解析と実行を分ける場合は ExecuteAsync に直接渡す。</summary>
    public SortAction Action { get; init; } = SortAction.Preview;
    public MultiMode Multi { get; init; } = MultiMode.MultipleFolder;

    /// <summary>false にすると Danbooru へは問い合わせず、キャッシュと簡易判定だけで仕分ける。</summary>
    public bool UseDanbooru { get; init; } = true;
    public bool ResolveAliases { get; init; } = true;
    public bool UnderscoreFolderNames { get; init; }
    public int MinPostCount { get; init; }

    /// <summary>「タグ,フォルダ名」形式の上書きリスト(任意)。</summary>
    public string? OverridesPath { get; init; }

    /// <summary>キャッシュファイルの場所。null なら既定の場所(LocalAppData)。</summary>
    public string? CachePath { get; init; }

    /// <summary>メタデータがない画像を、画像そのものからキャラクター推定する。</summary>
    public bool InferFromImage { get; init; }

    /// <summary>
    /// <see cref="InferFromImage"/> のとき、メタデータはあるがタグからキャラクターが見つからなかった画像 (_unknown になる画像) も推定する。
    /// </summary>
    public bool InferUnknown { get; init; } = true;

    /// <summary>使う推定モデル (<see cref="ModelCatalog"/> の Id)。</summary>
    public string TaggerModelId { get; init; } = ModelCatalog.Default.Id;

    /// <summary>この確信度以上のキャラクターだけ採用する (0.05〜0.99)。</summary>
    public double CharacterThreshold { get; init; } = 0.85;

    /// <summary>推定に GPU (DirectML) を使う。使えない場合は自動で CPU に切り替える。</summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>モデルの保存先。null なら既定の場所(LocalAppData)。</summary>
    public string? ModelsDir { get; init; }

    /// <summary>キャラクターが見つからなかった画像 (_unknown) もコピー/移動の対象にする。</summary>
    public bool IncludeUnknown { get; init; } = true;

    /// <summary>メタデータがない画像 (_no_metadata) もコピー/移動の対象にする。</summary>
    public bool IncludeNoMetadata { get; init; } = true;
}

public sealed class SortItem
{
    public required string Path { get; init; }
    public string Source { get; set; } = "";
    public List<string> Characters { get; } = new();

    /// <summary>画像から推定したキャラクター(確信度つき)。推定にかけた画像(メタデータがない画像など)でだけ入る。</summary>
    public List<TaggerPrediction> InferredCharacters { get; } = new();

    /// <summary>推定で、しきい値に届かなかったキャラクター(確信度の高い順)。手で仕分け先を選ぶときの候補。</summary>
    public List<TaggerPrediction> CandidateCharacters { get; } = new();

    /// <summary><see cref="CandidateCharacters"/> を仕分け先のフォルダ名にしたもの(上書きリストを反映、重複なし)。</summary>
    public List<string> CandidateFolders { get; } = new();

    public List<string> Folders { get; } = new();

    // 手で仕分け先を指定する前の、自動の仕分け先と対象外かどうか (戻すときに使う)
    private List<string>? _autoFolders;
    private bool _autoSkipped;

    /// <summary>仕分け先を手で指定した画像。</summary>
    public bool IsManual => _autoFolders != null;

    /// <summary>実行でコピー/移動した先 (実行すると入る)。</summary>
    public List<string> Destinations { get; } = new();
    public string Error { get; set; } = "";

    /// <summary>オプションで対象外にされた画像。実行してもコピー/移動しない。</summary>
    public bool Skipped { get; set; }

    /// <summary>一覧に出す状態 (実行待ち / 対象外 / コピー済 / 移動済 / 失敗: …)。</summary>
    public string Status { get; set; } = "";

    public bool HasMetadata => Source != PromptInfo.None && !Source.StartsWith("error", StringComparison.Ordinal);

    internal void SetManualFolder(string folder)
    {
        if (_autoFolders == null)
        {
            _autoFolders = new List<string>(Folders);
            _autoSkipped = Skipped;
        }
        Folders.Clear();
        Folders.Add(folder);
        Skipped = false; // 手で決めた画像は、_unknown などを対象外にする設定があっても実行する
        Status = "実行待ち";
    }

    internal void ResetToAutomatic()
    {
        if (_autoFolders == null) return;
        Folders.Clear();
        Folders.AddRange(_autoFolders);
        Skipped = _autoSkipped;
        Status = Skipped ? "対象外" : "実行待ち";
        _autoFolders = null;
    }
}

public sealed record SortProgress(string Phase, int Done, int Total);

public sealed class SortReport
{
    public List<SortItem> Items { get; } = new();

    /// <summary>Danbooru への問い合わせなどで問題があった場合のメッセージ。</summary>
    public string? Warning { get; set; }
}

/// <summary>
/// 解析の結果 (どの画像をどのフォルダに仕分けるか)。メモリ上に保持して、実行はこの内容をそのまま使う。
/// </summary>
public sealed class SortPlan
{
    /// <summary>解析したときの設定 (出力フォルダなど)。実行はこの設定で行う。</summary>
    public required SortOptions Options { get; init; }

    public List<SortItem> Items { get; } = new();

    /// <summary>実行前に知らせたいこと (推定に使った計算装置など)。</summary>
    public List<string> Notes { get; } = new();

    public string? Warning { get; set; }

    /// <summary>実行済み。移動した画像や重複コピーを避けるため、同じ結果は2度実行できない。</summary>
    public bool Executed { get; internal set; }

    /// <summary>実行でコピー/移動する画像の数 (対象外を除く)。</summary>
    public int TargetCount => Items.Count(i => !i.Skipped);

    /// <summary>この結果に出てくるキャラクターのフォルダ名 (特別なフォルダを除く、名前順)。手で指定するときの選択肢。</summary>
    public List<string> CharacterFolders() => Items
        .SelectMany(i => i.Folders.Concat(i.CandidateFolders))
        .Where(f => !f.StartsWith('_'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>
    /// 選んだ画像の仕分け先を、手で指定したフォルダ1つにする。対象外だった画像も実行の対象になる。
    /// 戻り値は、Windows で使えるように直したフォルダ名。
    /// </summary>
    public string AssignManually(IEnumerable<SortItem> items, string folder)
    {
        ThrowIfExecuted();
        string name = FolderNames.Sanitize(folder);
        foreach (var item in items)
            item.SetManualFolder(name);
        return name;
    }

    /// <summary>手で指定した仕分け先を取り消し、解析で決まった仕分け先に戻す。</summary>
    public void ResetToAutomatic(IEnumerable<SortItem> items)
    {
        ThrowIfExecuted();
        foreach (var item in items)
            item.ResetToAutomatic();
    }

    private void ThrowIfExecuted()
    {
        if (Executed)
            throw new InvalidOperationException("この解析結果はすでに実行済みです。もう一度解析してください。");
    }
}
