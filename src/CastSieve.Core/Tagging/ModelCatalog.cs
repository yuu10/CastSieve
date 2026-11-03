namespace CastSieve.Tagging;

/// <param name="Id">保存先フォルダ名にも使う識別子。</param>
/// <param name="Repo">Hugging Face のリポジトリ名 (owner/name)。</param>
public sealed record TaggerModel(string Id, string DisplayName, string Repo);

/// <summary>画像からタグを推定するモデル (WD Tagger 系) の一覧と保存場所。</summary>
public static class ModelCatalog
{
    public const string ModelFileName = "model.onnx";
    public const string TagsFileName = "selected_tags.csv";

    public static readonly IReadOnlyList<TaggerModel> All = new[]
    {
        new TaggerModel("wd-swinv2-tagger-v3", "WD SwinV2 v3(標準)", "SmilingWolf/wd-swinv2-tagger-v3"),
        new TaggerModel("wd-vit-tagger-v3", "WD ViT v3", "SmilingWolf/wd-vit-tagger-v3"),
        new TaggerModel("wd-eva02-large-tagger-v3", "WD EVA02-Large v3(大型)", "SmilingWolf/wd-eva02-large-tagger-v3"),
    };

    public static TaggerModel Default => All[0];

    public static TaggerModel Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? Default;

    public static string DefaultRoot() =>
        Path.Combine(AppPaths.DataRoot, "models");

    public static string DirectoryFor(TaggerModel model, string? root = null) =>
        Path.Combine(root ?? DefaultRoot(), model.Id);

    public static bool IsInstalled(TaggerModel model, string? root = null)
    {
        string dir = DirectoryFor(model, root);
        return File.Exists(Path.Combine(dir, ModelFileName)) && File.Exists(Path.Combine(dir, TagsFileName));
    }

    public static string DownloadUrl(TaggerModel model, string fileName) =>
        $"https://huggingface.co/{model.Repo}/resolve/main/{fileName}";
}
