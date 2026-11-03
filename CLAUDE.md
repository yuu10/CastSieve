# CastSieve (CLAUDE.md)

生成AI画像のメタデータ(プロンプト)からキャラクタータグを判別し、キャラクター別フォルダに仕分ける Windows アプリ。
C# / .NET 8 / WinForms。判別は Danbooru API(タグカテゴリ 4 = character)。メタデータがない画像は WD Tagger(ONNX)で画像から推定。現在のバージョンは `Directory.Build.props` の `<Version>` を見る(1.6.0)。1.6.0 で M Sorter から CastSieve に改名した(姉妹アプリは PromptLoupe、旧 MViewer)。

## 作業ルール
- 返信は日本語で、簡潔に。
- 説明は **アプリ内マニュアル `src/CastSieve.App/Help/manual.html`(利用者向け)と `README.md`(前半は公開向けの紹介、後半「開発者向けの情報」にビルド・仕組み・構成)の2つ**。これ以外のドキュメントは作らない(ライセンス文の `LICENSE` と `THIRD-PARTY-NOTICES.txt` は別)。
- パッケージを足したり、バージョンを上げたりしたら、`THIRD-PARTY-NOTICES.txt`(NuGet キャッシュのライセンス文を連結したもの)、README とマニュアルの「ライセンス」も直す。
- 機能を足したら、マニュアル(更新履歴を含む)、README、バージョン(`Directory.Build.props`)を一緒に更新する。更新履歴はマニュアルにだけ書く。
- 「実装しないで答えだけ」と言われたら、実装せず、可否と方針だけ答える。
- ビルド: `dotnet build CastSieve.sln`、テスト: `dotnet test`。変更したら必ずビルドとテストを通す(Claude.ai 版では SDK がなく確認できなかった)。
- 成果物を渡すときは、必要なら `CastSieve.zip`(先頭フォルダ `CastSieve/`、bin/obj を含めない)。
- Claude.ai 版では未コンパイルだった API(下の「未確認のリスク」)は、実機で確かめて直してよい。

## 構成
```
CastSieve/
  Directory.Build.props        Version, Nullable, ImplicitUsings
  src/CastSieve.Core/          net8.0 (読み取り・判別・仕分け)
    AppPaths.cs                %LOCALAPPDATA%\CastSieve、旧 MSorter フォルダの引き継ぎ
    Metadata/                  PNG tEXt/zTXt/iTXt, EXIF UserComment, ComfyUI, NovelAI V4, InvokeAI
    Tags/TagParser.cs          重み・強調・<lora:> を除去、小文字+空白に正規化
    Danbooru/                  DanbooruClient, TagCache, CharacterResolver
    Tagging/                   WdTagger, ModelCatalog, ModelDownloader, TaggerTagList
    Imaging/                   SkiaImage, ThumbnailMaker, LruCache
    Sorting/                   SortService, SortModels, FolderNames, OverrideFile
  src/CastSieve.App/           net8.0-windows (WinForms)
    MainForm.cs, ThumbnailListView.cs, ListViewColumnSorter.cs, AppSettings.cs, Program.cs
    Theme.cs, ThemedTabControl.cs  ダークモード(Palette, Theme.Apply, DarkToolStripRenderer)
    Help/HelpForm.cs
    Help/manual.html           埋め込みリソース(利用者向けの説明書)
    Assets/app.ico
  tests/CastSieve.Tests/       xUnit
  README.md                    開発者向けの説明
```
パッケージ: `Microsoft.ML.OnnxRuntime.DirectML` 1.20.1(MIT、依存の DirectML 1.15.2 は Microsoft 独自ライセンスだが再配布可)、`SkiaSharp` 4.154.0(MIT)。ライセンス文と一致させるため、バージョンは固定する。
- ライセンス: 本体は MIT(Copyright (c) 2026 yuto、PromptLoupe と同じ)。`LICENSE` と `THIRD-PARTY-NOTICES.txt` は App の出力先にコピーされる。WD Tagger のモデルは Apache-2.0 で、同梱しない。
- 1.3.3 で ImageSharp をやめた(4.x は Release ビルドにライセンスキーが必須になったため)。Mac 版を見据えて System.Drawing ではなく SkiaSharp にした。
- 画像処理は `Imaging/SkiaImage.cs` に集約(EXIF の向きは自前で回転、大きく縮小するときは先にミップマップ付き線形で縮めてからキュービック)。SkiaSharp は BMP を書き出せないので、`ThumbnailMaker` が 24 ビット BMP を自前で組み立てる。

## 仕様
### 流れ(解析と実行を分離)
- 「解析して確認」: `SortService.PlanAsync` が、読み取り → (画像から推定) → Danbooru 照会 → 仕分け先の決定。ファイルは触らない。結果は `SortPlan` としてメモリに保持。
- 「この結果で実行」: `SortService.ExecuteAsync(plan, action, ...)` が、プランのとおりコピーまたは移動。**再解析しない**。
  - 実行できるのは、プランがあり、`Executed == false` で、`TargetCount > 0` のときだけ。プランは1回しか実行できない。ファイルが消えていたら、その1件だけ「失敗」。
  - 実行後に `sort_log_日時.csv` を出力フォルダへ書く。
- 設定を変えるとプランは無効になる(一覧が灰色、実行ボタン無効、要約が消える)。**「操作」(コピー/移動)だけは、変えても無効にならない**。
- 手動指定(1.4.0): 一覧の右クリック / F2 で、選んだ画像の仕分け先を `SortPlan.AssignManually` で1フォルダに決める(対象外も対象になる、「(手動)」表示)。`ResetToAutomatic` で解析の結果に戻す。候補はしきい値未満の推定(0.1 以上、最大5人)= `CandidateCharacters` / `CandidateFolders`。しきい値の判定は `WdTagger` ではなく `SortService` で行う。
- 「中止」: `Task.Run` の中では例外を投げず、`IsCancellationRequested` でループを抜け、呼び出し側で `ThrowIfCancellationRequested()`(VS のデバッガーが止まるのを避けるため)。

### オプション(タブ: 基本 / 判別 (Danbooru) / 画像から推定 / 仕分け先 / 上書きリスト)
- 基本: サブフォルダも対象、操作(コピー/移動)、複数キャラの扱い(`_multiple` / 最初の1人 / 各フォルダにコピー)。
- 判別: Danbooru を使う / 別名タグも解決 / 最小投稿数。
- 画像から推定: 使う / モデル(wd-swinv2 既定、vit、eva02-large) / しきい値(既定 0.85) / GPU(DirectML、失敗したら CPU に自動切替)。
- 仕分け先: フォルダ名の空白を `_` に、`_unknown` も仕分ける(既定オン)、`_no_metadata` も仕分ける(既定オン)。オフにした画像は「対象外」。
- 上書きリスト: 「タグ,フォルダ名」の CSV/テキスト(任意)。
- タブの高さは一番高いタブに合わせる(`FitTabsHeight`)。
- 特別フォルダ: `_unknown`、`_no_metadata`、`_multiple`。

### Danbooru
- `tags.json` の `search[name_comma]`(40件ずつ)。失敗したら `name_matches` に切り替え。`tag_aliases.json` で別名を解決。
- キャッシュ: `%LOCALAPPDATA%\CastSieve\danbooru_tags.json`(見つかった=30日、見つからない=3日)。リクエスト間隔 250ms。User-Agent は `CastSieve/1.0`。
- 接続できないタグは「名前 (作品名)」形式の簡易判定にフォールバック。

### 画像からの推定(WD Tagger v3)
- 入力 448、NHWC、BGR、0〜255 の float、白で正方形に余白を足す。`selected_tags.csv` のカテゴリ 4 がキャラクター。
- モデルは Hugging Face(SmilingWolf)から `%LOCALAPPDATA%\CastSieve\models\<id>\`(`model.onnx`、`selected_tags.csv`)へ、確認のうえダウンロード。
- 推定で決まった画像は、メタデータ列が `image-ai`。

### 画面
- フォント: Yu Gothic UI 9.5pt(なければ Meiryo UI / Segoe UI)。`AutoScaleMode.Dpi`。
- ボタン: 解析=青の塗り、実行=白地に青枠、中止=薄いグレー。無効時は薄い色。
- 一覧: オーナードロー(常時)。縞模様、「状態」列の色(実行待ち=青 / 対象外=灰 / 完了=緑 / 失敗=赤)、列は ファイル / メタデータ / 検出キャラクター / 仕分け先 / 状態。
- 一覧の上に、解析・実行の件数を示す要約ラベル。
- 並べ替え: 見出しクリックで昇順/降順(▲/▼、`ListViewColumnSorter`、自然順)。処理中は並べ替えず `SetBusy(false)` でまとめて。ダブルクリック / Enter / 右クリック「ファイルを開く」で関連アプリで開く(1枚だけ)。
- フォルダ別グループ表示(「表示」メニュー、既定オン、折りたたみ可、`_` で始まるものは最後)。
- ドラッグ&ドロップ: 入力フォルダ欄・一覧・ウィンドウ=入力フォルダ(ファイルなら親フォルダ)、出力欄=出力フォルダ、上書きリスト欄=ファイル。
- サムネイル: メモリのみ。表示中の行だけ遅延読み込み(同時2枚)、LRU 300枚。設定は `%LOCALAPPDATA%\CastSieve\settings.json`。

## 注意(C#)
- ラムダの引数名を `_` にしたとき、同じラムダ内で `out _`(破棄)を使うと、引数の `_` と衝突してコンパイルエラーになる(1.3.1 で修正済み)。`out string _` のように型を書く。

## 未確認のリスク(Claude.ai 版ではコンパイル・実行していない)
- OnnxRuntime / DirectML のパッケージ名とバージョン、`AppendExecutionProvider_DML`。
- SkiaSharp への置き換え(1.3.3)で、推定の前処理の縮小方法が ImageSharp の Bicubic と少し違う。実機ではサムネイル(向き含む)OK、推定結果も体感で変化なし(厳密な比較はしていない)。
- Hugging Face のリポジトリ名とファイル名、WD モデルの前処理・出力形式。
- 大量の画像(数千枚以上)でのグループ表示の速度。

## 今後の候補
- 画像推定の精度(約半分が決まらない): **優先度を下げた**(2026-11)。決まらない画像の多くは候補すら出ず、モデルが知らないキャラが原因なので、反転の平均化などでは改善しない。推定はおまけで、本命はメタデータでの仕分け。やるなら見本画像との類似度で判定する方式。
- 公開に向けて: ライセンス表記、README の整理、スクリーンショット(`docs/images/main.png`)、配布の仕組みは済み。残りは、リポジトリ yuu10/CastSieve の Public 化と、最初のリリース(タグ v1.6.0)。
- 配布: `.github/workflows/build.yml`(main と PR でビルド+テスト)、`release.yml`(タグ `v*` で自己完結の単一 exe を発行し、zip を Releases に下書き。タグと `Directory.Build.props` の Version が違うと止まる)。zip には exe・LICENSE・THIRD-PARTY-NOTICES.txt だけ(publish に出る libSkiaSharp.pdb と onnxruntime.lib は消す)。ネイティブ DLL は `IncludeNativeLibrariesForSelfExtract` で exe に入れる。単一 exe では `Assembly.Location` が空になるので使わない。
- exe のアイコンが変わらない: クリーン→リビルド、アイコンキャッシュ(`ie4uinit.exe -show`)、別フォルダへコピー。
- ダークモード(1.5.0): .NET 8 のまま自前で色を当てる(`Theme.cs`)。「表示」→「テーマ」でシステム / ライト / ダーク、既定はシステム。標準ダイアログと NumericUpDown の上下ボタンは明るいまま。
- 制限: NovelAI のステルス PNGInfo(アルファチャンネル埋め込み)は未対応。

## バージョン履歴(公開前の経緯。マニュアルの更新履歴は 1.6.0「最初の公開版」から始まる)
1.6.0 CastSieve に改名・旧データフォルダの引き継ぎ・アイコン変更(ふるい)・ライセンス表記 / 1.5.0 ダークモード / 1.4.0 仕分け先の手動指定・推定の候補表示・列の並べ替え・ファイルを開く / 1.3.3 ImageSharp → SkiaSharp / 1.3.2 上書きリストをタブ化、タブ高さを統一 / 1.3.1 ビルドエラー修正、ImageSharp 4.1.1 / 1.3.0 見た目改修・タブ化・D&D・要約・グループ表示 / 1.2.1 中止時の例外停止を修正 / 1.2.0 アイコン変更 / 1.1.0 解析と実行の分離・GPU 対応 / 1.0.0 初版
