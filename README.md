# CastSieve(キャラクタータグ仕分けツール / C# / .NET 8)

生成AI画像に埋め込まれたプロンプトからタグを取り出し、**Danbooru のタグカテゴリ(character)** でキャラクターを自動判別して、キャラクター別フォルダに仕分ける Windows アプリです。

## ビルドと実行

必要なもの: .NET 8 SDK(Windows)

```
dotnet test                                  # テスト(Core のみ。Linux/macOS でも可)
dotnet run --project src/CastSieve.App # GUI を起動
dotnet publish src/CastSieve.App -c Release -r win-x64 --self-contained false
```

`CastSieve.sln` を Visual Studio 2022 で開いてもビルドできます。

## 使い方

1. 入力フォルダと出力フォルダを選ぶ
2. 「解析して確認」で仕分け結果を確認(ファイルは変更されません)
3. 必要なら、一覧で右クリック(または `F2`)して、選んだ画像の仕分け先を手で直す(下の「仕分け先の手動指定」)
4. 問題なければ「この結果で実行」(コピー or 移動)。再解析はせず、確認した結果のとおりに仕分けます。設定を変えると結果は無効になり、解析し直しが必要です(「操作」の切り替えを除く)

アプリ内のマニュアルは、メニューの「ヘルプ」→「使い方」(`F1`)で開けます。マニュアルの元になっているのは `src/CastSieve.App/Help/manual.html` で、内容を直したいときはこのファイルを編集してください(アプリに埋め込まれます)。アイコンは `src/CastSieve.App/Assets/app.ico` です。

実行すると出力フォルダに `sort_log_日時.csv`(元ファイル・メタデータ種別・検出キャラクター・移動先)が保存されます。

## キャラクター判別の仕組み

1. 画像のメタデータからポジティブプロンプトを取り出し、重み `(tag:1.2)`、強調 `[]{}`、`<lora:...>` を除いてタグに分解
2. 未知のタグをまとめて Danbooru API に照会し、**カテゴリ 4 (character)** のものをキャラクターとみなす
   - 一括照会 `tags.json?search[name_comma]=...`(40 タグずつ)。サーバーが受け付けない場合は 1 タグずつの照会に自動で切り替え
   - Danbooru に存在しないタグは、有効な**別名 (tag alias)** を探して正規タグで再判定(`ganyu` → `ganyu_(genshin_impact)`)
3. 結果は `%LOCALAPPDATA%\CastSieve\danbooru_tags.json` にキャッシュ(見つかったタグ 30 日、見つからなかったタグ 3 日)。2 回目以降は通信しません
4. Danbooru に接続できなかったタグだけ、「名前 (作品名)」形式の簡易判定にフォールバック(`(cosplay)` `(weapon)` などは除外)

**通信内容**: Danbooru に送るのはタグ名だけで、画像や画像のパスは送りません。リクエスト間隔は 250ms 以上空け、429/5xx は待って再試行します。

## 仕分け先の手動指定

解析のあと、実行の前に、一覧で選んだ画像の仕分け先を手で決められます。

- `SortPlan.AssignManually(items, folder)` が仕分け先をそのフォルダ1つにします。フォルダ名は `FolderNames.Sanitize` で直します。対象外だった画像も実行の対象になります
- `SortPlan.ResetToAutomatic(items)` で、解析で決まった仕分け先と対象外の状態に戻します。何回指定しても、戻る先は解析の結果です
- 実行済みのプランに対しては、どちらも `InvalidOperationException` を投げます
- 手で決めた内容はプランの中にだけあるので、設定を変えたり解析し直したりすると消えます
- 候補: 推定でしきい値に届かなかったキャラクター(確信度 0.1 以上、最大5人)を `SortItem.CandidateCharacters` に残し、上書きリストを反映したフォルダ名を `CandidateFolders` に入れます。右クリックメニューの「候補: …」と、ダイアログの選択肢に使います
- `IImageTagger.PredictCharacters` はしきい値で絞らず返し、しきい値による採用と候補の振り分けは `SortService` が行います

## 一覧の並べ替えと、画像を開く

- 見出しをクリックすると、その列で昇順/降順を切り替えます(`ListViewColumnSorter`)。比較は `StrCmpLogicalW` を使うのでエクスプローラーと同じ順になり、同じ値どうしはファイル名で並べます。見出しに ▲/▼ を付けます
- 解析中や実行中は並べ替えず(行を足すたびに並べ替わって遅くなるため)、終わってからまとめて並べ替えます。標準(どの列もクリックしていないとき)は解析した順で、ファイル名の昇順です
- 行のダブルクリック・`Enter`(`ItemActivate`)と右クリックの「ファイルを開く」で、`UseShellExecute` で関連付けられたアプリで開きます。開くのはフォーカスのある1枚だけです。移動した後は移動先を開きます

## サムネイル表示

結果の一覧に画像のサムネイルを表示します。メニュー「表示」で、オン/オフとサイズ(48 / 80 / 120px)を切り替えられます(`%LOCALAPPDATA%\CastSieve\settings.json` に保存。サムネイル自体は保存しません)。

- サムネイルはメモリ上だけで作り、ディスクには書き出しません(`ThumbnailMaker` が SkiaSharp で縮小し、BMP のバイト列として返します)
- ListView のオーナードローで描画し、**画面に見えている行だけ**を非同期で読み込みます(同時に2枚まで)。見えなくなった行の読み込みは取りやめます
- 作ったサムネイルは LRU キャッシュ(300枚)に置き、超えた分は破棄します
- オフのときはオーナードロー自体を止めるので、サムネイル関連の処理は動きません

## 画像からの推定(メタデータがない画像用)

オプション「メタデータなしの画像は画像から推定」をオンにすると、メタデータのない画像を [WD Tagger](https://huggingface.co/SmilingWolf)(SmilingWolf)の ONNX モデルにかけ、キャラクタータグを推定します。

- 推論は `Microsoft.ML.OnnxRuntime.DirectML` で、GPU(DirectML)を選べます。GPU で失敗したら CPU に自動で切り替えます
- モデルは wd-swinv2(標準)/ vit / eva02-large から選べます
- 画像の読み込みと前処理は `SkiaSharp` で行います(EXIF の向きを反映 → 白で正方形に余白を足す → 448px に縮小 → BGR・0〜255 の float、NHWC)
- モデルは初回に Hugging Face から `%LOCALAPPDATA%\CastSieve\models\<モデル名>\` へダウンロードします(`model.onnx` と `selected_tags.csv`)。手動で置いても使えます
- メタデータのある画像は推定しません。推定結果は確信度(しきい値 標準 0.85)以上のキャラクターだけ採用し、上書きリストを反映します
- 推定で決まった画像は、一覧とログの「メタデータ」列が `image-ai` になります
- ライセンス: SkiaSharp は MIT ライセンスです。各モデルのライセンスは配布元で確認してください

## テーマ(ダークモード)

メニュー「表示」→「テーマ」で、システムに合わせる(既定)/ ライト / ダークを選びます(`settings.json` の `Theme`)。.NET 8 の WinForms には標準のダークモードがないので、自前で色を当てています(`Theme.cs`)。

- 色は `Palette`(`Light` / `Dark`)にまとめ、`Theme.Apply` がコントロールをたどって当てます。ライトのときは `ResetBackColor` などで既定の色に戻します
- ボタンは `Tag` の `ButtonRole`、集計ラベルは `ChipKind`、補足の文字は `Theme.SubTextTag` で役割を持たせ、テーマを切り替えたら色を当て直します
- タイトルバーは `DwmSetWindowAttribute`(属性 20)。入力欄・ComboBox は `SetWindowTheme("DarkMode_CFD")`、一覧は `DarkMode_Explorer`(見出しは `DarkMode_ItemsView`)でスクロールバーなどを暗くします。ハンドルが作り直されても当たるよう、`HandleCreated` で当て直します
- 一覧の列の見出しは、ダークのときだけ自分で描きます。グループ見出しはダークのテーマだと紺色で読めないため、`ThumbnailListView` が NM_CUSTOMDRAW の後で文字だけ描き直します(文字色の指定は効かない)
- タブは `ThemedTabControl` が、ダークのときだけ `UserPaint` で見出しと枠を描きます
- メニューは `DarkToolStripRenderer`(`ToolStripManager.Renderer` を差し替え)。チェックの印は自分で描きます
- 進行バーはテーマを外して(`SetWindowTheme("", "")`)、色を指定します
- マニュアルは WebBrowser(IE のエンジン)で `prefers-color-scheme` が使えないため、ダーク用の CSS を `</head>` の前に足して表示します
- 「システムに合わせる」のときは `SystemEvents.UserPreferenceChanged` で Windows の切り替えに追従します(判定はレジストリの `AppsUseLightTheme`)
- 制限: MessageBox やフォルダ選択などの標準ダイアログ、NumericUpDown の上下ボタンは明るいまま

## オプション

オプションはタブで分かれています。

| タブ | 項目 | 説明 |
|---|---|---|
| 基本 | サブフォルダも対象 | 入力フォルダの下のフォルダも読む |
| 基本 | 操作 | コピー / 移動 |
| 基本 | 複数キャラ | `_multiple` に入れる / 最初の1人のみ / 各フォルダにコピー |
| 判別 (Danbooru) | Danbooruで判別 | オフにすると通信せず、キャッシュと簡易判定のみで仕分け |
| 判別 (Danbooru) | 別名タグも解決 | 初回のみ時間がかかる(存在しないタグ 1 つにつき 1 リクエスト) |
| 判別 (Danbooru) | 最小投稿数 | 投稿数がこの値未満のキャラクタータグを無視(誤検出対策) |
| 画像から推定 | 使う / モデル / しきい値 / GPU | 上の「画像からの推定」を参照 |
| 仕分け先 | 空白を `_` に | フォルダ名の空白を `_` に置き換える |
| 仕分け先 | `_unknown` / `_no_metadata` も仕分ける | オフにすると、その画像は「対象外」になり動かさない |
| 上書きリスト | ファイル | `タグ,フォルダ名` の CSV。Danbooru の判定より優先(例: `hatsune miku,初音ミク`) |

## 仕分け先の特別フォルダ

`_unknown`(キャラクタータグなし)/ `_no_metadata`(メタデータなし)/ `_multiple`(複数キャラクター)

## 対応メタデータ

- AUTOMATIC1111 / Forge: PNG `parameters`、JPEG/WebP の Exif UserComment
- NovelAI: PNG `Comment` / `Description`(V4 のキャラクター別プロンプトを含む)
- ComfyUI: PNG `prompt`(KSampler の positive 接続を辿り、ネガティブを除外)
- InvokeAI: PNG `invokeai_metadata`

## 構成

```
src/CastSieve.Core   読み取り・タグ解析・Danbooru 連携・仕分け (net8.0)
src/CastSieve.App    WinForms の画面 (net8.0-windows)
tests/CastSieve.Tests xUnit (Danbooru は偽サーバーで検証)
```

設定 (`settings.json`)、Danbooru のキャッシュ、モデルは `%LOCALAPPDATA%\CastSieve\` に置きます(`AppPaths.DataRoot`)。1.6.0 で M Sorter から改名したため、起動時に `AppPaths.MigrateFromOldName()` が、旧フォルダ `%LOCALAPPDATA%\MSorter\` があって新フォルダがないときだけ、フォルダごと移します。

## 制限事項

- NovelAI の「ステルス PNGInfo」(アルファチャンネル埋め込み)は未対応
- SNS 等でメタデータが削除された画像は、標準では `_no_metadata` に入ります(「画像から推定」をオンにすると推定できます)
- 画像からの推定は、投稿数の多い有名なキャラクターほど当たりやすく、マイナーなキャラクターや自作キャラクターは苦手です
- `solo` のようなタグがなくても、プロンプトに複数キャラが含まれれば複数キャラ扱いになります
