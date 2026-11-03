using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using CastSieve.Imaging;
using CastSieve.Sorting;
using CastSieve.Tagging;

namespace CastSieve.App;

public sealed class MainForm : Form
{
    private readonly TextBox _input = new();
    private readonly TextBox _output = new();
    private readonly TextBox _overrides = new();

    // 読み込み
    private readonly CheckBox _recursive = new() { Text = "サブフォルダも対象", AutoSize = true };

    // キャラクター判別 (Danbooru)
    private readonly CheckBox _useDanbooru = new() { Text = "Danbooruで判別", AutoSize = true, Checked = true };
    private readonly CheckBox _aliases = new() { Text = "別名タグも解決", AutoSize = true, Checked = true };
    private readonly NumericUpDown _minPosts = new() { Minimum = 0, Maximum = 1_000_000, Value = 0, Width = 70 };

    // 画像から推定
    private readonly CheckBox _infer = new() { Text = "メタデータなしの画像は画像から推定", AutoSize = true };
    private readonly CheckBox _inferUnknown = new()
    {
        Text = "タグでキャラクターが分からない画像も推定", AutoSize = true, Checked = true, Enabled = false,
    };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Enabled = false };
    private readonly NumericUpDown _threshold = new()
    {
        DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.30m, Maximum = 0.99m, Value = 0.85m, Width = 60, Enabled = false,
    };
    private readonly CheckBox _useGpu = new() { Text = "GPUを使う(使えなければCPU)", AutoSize = true, Checked = true, Enabled = false };

    // 仕分け
    private readonly ComboBox _action = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _multi = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _underscore = new() { Text = "フォルダ名の空白を_に", AutoSize = true };
    private readonly CheckBox _includeUnknown = new()
    {
        Text = "キャラクター不明の画像も仕分ける(_unknown)", AutoSize = true, Checked = true,
    };
    private readonly CheckBox _includeNoMetadata = new()
    {
        Text = "メタデータなしの画像も仕分ける(_no_metadata)", AutoSize = true, Checked = true,
    };

    private readonly Button _analyzeButton = new() { Text = "解析して確認(ファイルは変更しない)", AutoSize = true };
    private readonly Button _runButton = new() { Text = "この結果で実行", AutoSize = true, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "中止", AutoSize = true, Enabled = false };

    private readonly ListView _list = new ThumbnailListView()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        OwnerDraw = true, // 行の縞模様と「状態」の色を自分で描く
        Dock = DockStyle.Fill,
    };
    private static readonly string[] ColumnTitles = { "ファイル", "メタデータ", "検出キャラクター", "仕分け先", "状態" };

    // 見出しをクリックして選んだ並べ替え (-1 = 解析した順 = ファイル名の昇順)
    private int _sortColumn = -1;
    private bool _sortDescending;

    // 設定を変えたか中止して、一覧の解析結果が無効になっている(灰色で描く)
    private bool _stale;

    // 解析後の集計(件数・キャラクター数など)を、小さなラベルで並べる
    private readonly FlowLayoutPanel _summary = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        WrapContents = true,
        Visible = false,
        Margin = new Padding(0, 2, 0, 4),
    };

    // 仕分け先のフォルダごとのグループ (表示のオン/オフは「表示」メニュー)
    private readonly Dictionary<string, ListViewGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Fill, Height = 18, Style = ProgressBarStyle.Continuous };
    private readonly Label _status = new() { Text = "フォルダを選んで「解析して確認」を押してください", AutoSize = true };

    private Control? _pathPanel;
    private Control? _optionsPanel;

    private CancellationTokenSource? _cts;
    private HelpForm? _helpForm;

    // ---- 解析の結果 ----
    // 「解析して確認」の結果をメモリに保持し、「この結果で実行」はこの内容のままコピー/移動する。
    // 設定を変えると結果は無効になり、もう一度解析するまで実行できない。
    private SortPlan? _plan;
    private bool _busy;
    private readonly Dictionary<SortItem, ListViewItem> _rows = new();

    // ---- サムネイル(メモリ上だけ。ディスクには保存しない) ----
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly LruCache<string, Bitmap?> _thumbCache =
        new(300, bitmap => bitmap?.Dispose(), StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingThumbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _thumbGate = new(2); // 同時に作るのは2枚まで
    private int _thumbGeneration; // 設定変更や新しい実行のたびに増やし、古い読み込み結果を捨てる
    private bool _showThumbnails;
    private int _thumbSize = 80;

    public MainForm()
    {
        Text = "CastSieve";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = PickFont();
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // アイコンを取得できなくても動作に影響はない
        }
        Width = 1080;
        Height = 800;
        MinimumSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _action.Items.AddRange(new object[] { "コピー", "移動" });
        _action.SelectedIndex = 0;
        _multi.Items.AddRange(new object[] { "_multipleフォルダへ", "最初の1人のみ", "各フォルダにコピー" });
        _multi.SelectedIndex = 0;
        foreach (var model in ModelCatalog.All)
            _model.Items.Add(model.DisplayName);
        _model.SelectedIndex = 0;

        int[] widths = { 240, 80, 240, 220, 160 };
        for (int i = 0; i < ColumnTitles.Length; i++)
            _list.Columns.Add(ColumnTitles[i], widths[i]);

        _pathPanel = BuildPathTable();
        _optionsPanel = BuildOptionsPanel();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(14, 10, 14, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 入力・出力フォルダ
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // オプション(タブ)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // ボタン
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 集計
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 結果の一覧
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 進行バー
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 状態

        root.Controls.Add(_pathPanel, 0, 0);
        root.Controls.Add(_optionsPanel, 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);
        root.Controls.Add(_summary, 0, 3);
        root.Controls.Add(_list, 0, 4);
        root.Controls.Add(_bar, 0, 5);
        root.Controls.Add(_status, 0, 6);
        Controls.Add(root);
        Controls.Add(BuildMenu()); // root より後に追加すると、メニューが先に上端へ配置される

        _analyzeButton.Click += async (_, _) => await AnalyzeAsync();
        _runButton.Click += async (_, _) => await ExecutePlanAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _useDanbooru.CheckedChanged += (_, _) => _aliases.Enabled = _useDanbooru.Checked;
        _infer.CheckedChanged += (_, _) =>
            _inferUnknown.Enabled = _model.Enabled = _threshold.Enabled = _useGpu.Enabled = _infer.Checked;

        // 「操作(コピー/移動)」以外の設定を変えたら、解析の結果を無効にする
        Watch(_input, _output, _overrides, _recursive, _useDanbooru, _aliases, _minPosts,
            _infer, _inferUnknown, _model, _threshold, _useGpu, _multi, _underscore, _includeUnknown, _includeNoMetadata);

        _list.DrawColumnHeader += OnDrawColumnHeader;
        _list.DrawItem += (_, _) => { }; // 詳細表示では、各列を DrawSubItem で描く
        _list.DrawSubItem += OnDrawSubItem;
        _list.ShowGroups = _settings.GroupByFolder;
        _list.ContextMenuStrip = BuildListMenu();
        _list.ColumnClick += (_, e) => OnColumnClick(e.Column);
        _list.ItemActivate += (_, _) => OpenFile(_list.FocusedItem?.Tag as SortItem); // ダブルクリックか Enter
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F2) return;
            e.Handled = true;
            AssignSelected();
        };
        ApplyThumbnailSettings();

        // フォルダやファイルをドラッグ&ドロップで指定できる(処理中は受け付けない)
        _input.PlaceholderText = "ここへフォルダをドラッグ&ドロップ、または「参照...」";
        _output.PlaceholderText = "仕分けた画像の保存先(入力フォルダを選ぶと、その中の sorted が自動で入ります)";
        _overrides.PlaceholderText = "「タグ,フォルダ名」の CSV/テキスト(なくても動きます)";
        EnableDrop(_input, SetInputFolder);
        EnableDrop(_list, SetInputFolder);
        EnableDrop(this, SetInputFolder);
        EnableDrop(_output, path => _output.Text = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
        EnableDrop(_overrides, path => { if (File.Exists(path)) _overrides.Text = path; });

        // 長い状態メッセージは折り返して表示する
        _status.MaximumSize = new Size(ClientSize.Width - 30, 0);
        Resize += (_, _) => _status.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 30), 0);

        UpdateButtons();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _thumbGeneration++;
        _thumbCache.Dispose();
        base.OnFormClosed(e);
    }

    // ---- サムネイル --------------------------------------------------------

    /// <summary>表示設定を一覧に反映する。作り済みのサムネイルは捨てて、見えている行から作り直す。</summary>
    private void ApplyThumbnailSettings()
    {
        _thumbGeneration++;
        _thumbCache.Clear();
        _showThumbnails = _settings.ShowThumbnails;
        _thumbSize = _settings.ThumbnailSize;

        var oldImageList = _list.SmallImageList;
        // 中身のない ImageList は、行の高さを決めるためだけに使う(サムネイルがあるときはその大きさ、ないときは少しゆとりのある高さ)
        int rowHeight = _showThumbnails ? _thumbSize + 4 : LogicalToDeviceUnits(28);
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, Math.Min(256, rowHeight)) };
        oldImageList?.Dispose();
        _list.Invalidate();
    }

    private void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;

        var cell = e.Bounds;
        if (e.ColumnIndex == 0) cell.Width = _list.Columns[0].Width; // 1列目は行全体の幅で渡されることがある

        var p = Theme.Current;
        bool selected = e.Item.Selected;
        Color back = selected ? p.Selection : (e.ItemIndex % 2 == 1 ? p.Zebra : _list.BackColor);
        using (var background = new SolidBrush(back))
            e.Graphics.FillRectangle(background, cell);

        var textBounds = new Rectangle(cell.X + 6, cell.Y, Math.Max(0, cell.Width - 6), cell.Height);
        if (e.ColumnIndex == 0 && _showThumbnails)
        {
            var box = new Rectangle(cell.X + 4, cell.Y + 2, _thumbSize, _thumbSize);
            DrawThumbnail(e.Graphics, e.Item, box);
            textBounds = new Rectangle(box.Right + 8, cell.Y, Math.Max(0, cell.Right - box.Right - 8), cell.Height);
        }

        Color text;
        if (selected) text = p.SelectionText;
        else if (_stale || (e.Item.Tag as SortItem)?.Skipped == true) text = p.GrayText;
        else if (e.ColumnIndex == 4) text = StatusColor(e.SubItem.Text);
        else text = p.Text;

        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem.Text,
            _list.Font,
            textBounds,
            text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>「状態」列の色。実行待ち=青、対象外=灰、完了=緑、失敗=赤。</summary>
    private static Color StatusColor(string status) => status switch
    {
        "実行待ち" => Theme.Current.Accent,
        "対象外" => Theme.Current.GrayText,
        "コピー済" or "移動済" => Theme.Current.StatusDone,
        _ when status.StartsWith("失敗", StringComparison.Ordinal) => Theme.Current.StatusFailed,
        _ => Theme.Current.Text,
    };

    /// <summary>ライトのときは標準の見出し。ダークのときは標準の見出しが白いままなので、自分で描く。</summary>
    private void OnDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = Theme.Current;
        if (!p.IsDark || e.Header == null)
        {
            e.DrawDefault = true;
            return;
        }

        var bounds = e.Bounds;
        using (var back = new SolidBrush(p.HeaderBack))
            e.Graphics.FillRectangle(back, bounds);
        using (var line = new Pen(p.Border))
        {
            e.Graphics.DrawLine(line, bounds.Right - 1, bounds.Top + 4, bounds.Right - 1, bounds.Bottom - 5);
            e.Graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }
        TextRenderer.DrawText(
            e.Graphics,
            e.Header.Text,
            _list.Font,
            new Rectangle(bounds.X + 6, bounds.Y, Math.Max(0, bounds.Width - 10), bounds.Height),
            p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ---- テーマ ------------------------------------------------------------

    private void ApplyTheme()
    {
        Theme.Use(Theme.Resolve(_settings.Theme));
        Theme.Apply(this);
        if (MainMenuStrip != null) MainMenuStrip.Font = Font; // レンダラーを替えると、フォントが既定に戻ることがある
        _helpForm?.ApplyTheme();
        Invalidate(true);
    }

    /// <summary>「システムに合わせる」のときは、Windows のライト/ダークの切り替えに追従する。</summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme != ThemeMode.System || !IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            if (Theme.Resolve(ThemeMode.System).IsDark != Theme.Current.IsDark) ApplyTheme();
        });
    }

    private void DrawThumbnail(Graphics g, ListViewItem item, Rectangle box)
    {
        if (item.Tag is not SortItem sortItem) return;

        if (_thumbCache.TryGet(sortItem.Path, out var bitmap) && bitmap != null)
        {
            g.DrawImageUnscaled(
                bitmap,
                box.X + (box.Width - bitmap.Width) / 2,
                box.Y + (box.Height - bitmap.Height) / 2);
            return;
        }

        // まだ読み込んでいない、または読み込めなかった画像は、灰色の枠を出しておく
        using (var fill = new SolidBrush(Theme.Current.ThumbFill))
            g.FillRectangle(fill, box);
        using (var border = new Pen(Theme.Current.ThumbBorder))
            g.DrawRectangle(border, box.X, box.Y, box.Width - 1, box.Height - 1);

        if (!_thumbCache.Contains(sortItem.Path))
            RequestThumbnail(item, sortItem);
    }

    private void RequestThumbnail(ListViewItem item, SortItem sortItem)
    {
        if (!_pendingThumbs.Add(sortItem.Path)) return;
        _ = LoadThumbnailAsync(item, sortItem, _thumbGeneration, _thumbSize);
    }

    private async Task LoadThumbnailAsync(ListViewItem item, SortItem sortItem, int generation, int size)
    {
        string key = sortItem.Path;
        try
        {
            await _thumbGate.WaitAsync();
            try
            {
                // 待っている間にスクロールや設定変更があったら、読み込まずにやめる
                if (IsDisposed || generation != _thumbGeneration) return;
                if (item.ListView == null || !item.Bounds.IntersectsWith(_list.ClientRectangle)) return;

                string path = CurrentFilePath(sortItem);
                byte[]? data = await Task.Run(() => ThumbnailMaker.TryCreateBmp(path, size));
                if (IsDisposed || generation != _thumbGeneration) return;

                Bitmap? bitmap = null;
                if (data != null)
                {
                    try
                    {
                        bitmap = BitmapFromBytes(data);
                    }
                    catch (ArgumentException)
                    {
                        // 画像として読めなかった場合は、灰色の枠のままにする(null を覚えて再試行しない)
                    }
                }

                _thumbCache.Set(key, bitmap);
                if (item.ListView != null) _list.Invalidate(item.Bounds);
            }
            finally
            {
                _thumbGate.Release();
            }
        }
        finally
        {
            _pendingThumbs.Remove(key);

            // 読み込み中に設定が変わって結果を捨てた場合、見えている行を描き直して改めて読み込ませる
            if (!IsDisposed && generation != _thumbGeneration && item.ListView != null)
                _list.Invalidate(item.Bounds);
        }
    }

    /// <summary>移動した画像は、元の場所にはもうないので、移動先から読む。</summary>
    private static string CurrentFilePath(SortItem item) =>
        !File.Exists(item.Path) && item.Destinations.Count > 0 ? item.Destinations[^1] : item.Path;

    private static Bitmap BitmapFromBytes(byte[] data)
    {
        // Bitmap は元のストリームを開いたままにする必要があるので、コピーを作ってストリームを閉じる
        using var stream = new MemoryStream(data);
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private MenuStrip BuildMenu()
    {
        var manual = new ToolStripMenuItem("使い方(&U)") { ShortcutKeys = Keys.F1 };
        manual.Click += (_, _) => ShowHelp();

        var about = new ToolStripMenuItem("CastSieve について(&A)");
        about.Click += (_, _) => MessageBox.Show(
            this,
            $"CastSieve\nバージョン {Application.ProductVersion.Split('+')[0]}\n\nDanbooru のタグ情報でキャラクターを判別して、画像を仕分けます。",
            "CastSieve について",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        var help = new ToolStripMenuItem("ヘルプ(&H)");
        help.DropDownItems.Add(manual);
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(about);

        var thumbnails = new ToolStripMenuItem("サムネイルを表示(&T)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowThumbnails,
        };
        thumbnails.CheckedChanged += (_, _) =>
        {
            _settings.ShowThumbnails = thumbnails.Checked;
            _settings.Save();
            ApplyThumbnailSettings();
        };

        var sizeMenu = new ToolStripMenuItem("サムネイルの大きさ(&S)");
        foreach (var (label, pixels) in new[] { ("小(48px)", 48), ("中(80px)", 80), ("大(120px)", 120) })
        {
            var sizeItem = new ToolStripMenuItem(label) { Checked = _settings.ThumbnailSize == pixels };
            sizeItem.Click += (_, _) =>
            {
                _settings.ThumbnailSize = pixels;
                _settings.Save();
                foreach (ToolStripMenuItem other in sizeMenu.DropDownItems)
                    other.Checked = ReferenceEquals(other, sizeItem);
                ApplyThumbnailSettings();
            };
            sizeMenu.DropDownItems.Add(sizeItem);
        }

        var grouping = new ToolStripMenuItem("フォルダ別にグループ表示(&G)")
        {
            CheckOnClick = true,
            Checked = _settings.GroupByFolder,
        };
        grouping.CheckedChanged += (_, _) =>
        {
            _settings.GroupByFolder = grouping.Checked;
            _settings.Save();
            _list.ShowGroups = grouping.Checked;
            // オフ→オンに戻すと、行とグループのつながりが表示に反映されず一覧が空に見えるため、グループを作り直す
            if (grouping.Checked) FinalizeGroups();
        };

        var themeMenu = new ToolStripMenuItem("テーマ(&M)");
        foreach (var (label, mode) in new[]
                 {
                     ("システムに合わせる(&S)", ThemeMode.System), ("ライト(&L)", ThemeMode.Light), ("ダーク(&D)", ThemeMode.Dark),
                 })
        {
            var themeItem = new ToolStripMenuItem(label) { Checked = _settings.Theme == mode };
            themeItem.Click += (_, _) =>
            {
                _settings.Theme = mode;
                _settings.Save();
                foreach (ToolStripMenuItem other in themeMenu.DropDownItems)
                    other.Checked = ReferenceEquals(other, themeItem);
                ApplyTheme();
            };
            themeMenu.DropDownItems.Add(themeItem);
        }

        var view = new ToolStripMenuItem("表示(&V)");
        view.DropDownItems.Add(grouping);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(thumbnails);
        view.DropDownItems.Add(sizeMenu);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(themeMenu);

        var menu = new MenuStrip { Font = Font };
        menu.Items.Add(view);
        menu.Items.Add(help);
        MainMenuStrip = menu;
        return menu;
    }

    private void ShowHelp()
    {
        if (_helpForm == null || _helpForm.IsDisposed)
        {
            _helpForm = new HelpForm();
            _helpForm.Show(this);
        }
        else
        {
            _helpForm.Activate();
        }
    }

    private TableLayoutPanel BuildPathTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddPathRow(table, 0, "入力フォルダ", _input, BrowseFolder, suggestOutput: true);
        AddPathRow(table, 1, "出力フォルダ", _output, BrowseFolder);
        return table;
    }

    private void AddPathRow(
        TableLayoutPanel table, int row, string label, TextBox box, Func<string?> browse, bool suggestOutput = false)
    {
        table.Controls.Add(
            new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) }, 0, row);
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(box, 1, row);

        var button = new Button { Text = "参照...", AutoSize = true };
        button.Click += (_, _) =>
        {
            string? path = browse();
            if (string.IsNullOrEmpty(path)) return;
            box.Text = path;
            if (suggestOutput && string.IsNullOrWhiteSpace(_output.Text))
                _output.Text = Path.Combine(path, "sorted");
        };
        table.Controls.Add(button, 2, row);
    }

    private TabControl BuildOptionsPanel()
    {
        // 設定は内容ごとにタブへ分ける。タブの中は横に並べ、入らなければ次の行へ折り返す
        var tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            Height = LogicalToDeviceUnits(116),
            MinimumSize = new Size(0, LogicalToDeviceUnits(116)), // 自動サイズの行で潰れないようにする
            Margin = new Padding(0, 8, 0, 8),
        };
        tabs.TabPages.Add(Page("基本", _recursive, Row(Caption("操作"), _action), Row(Caption("複数キャラ"), _multi)));
        tabs.TabPages.Add(Page("判別 (Danbooru)", _useDanbooru, _aliases, Row(Caption("最小投稿数"), _minPosts)));
        tabs.TabPages.Add(Page(
            "画像から推定", _infer, _inferUnknown, Row(Caption("モデル"), _model), Row(Caption("しきい値"), _threshold), _useGpu));
        tabs.TabPages.Add(Page("仕分け先", _underscore, _includeUnknown, _includeNoMetadata));
        tabs.TabPages.Add(BuildOverridesPage());

        // 高さは一番高いタブに合わせる(タブを切り替えても一覧が動かない)
        tabs.HandleCreated += (_, _) => FitTabsHeight(tabs);
        tabs.SizeChanged += (_, _) => FitTabsHeight(tabs);
        // 起動直後はフォントや DPI の拡大がまだ反映されておらず低く測れるので、表示されたあとにも測り直す
        Shown += (_, _) => FitTabsHeight(tabs);
        return tabs;
    }

    private TabPage BuildOverridesPage()
    {
        _overrides.Width = LogicalToDeviceUnits(420);
        _overrides.Margin = new Padding(3, 3, 3, 3);

        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            string? path = BrowseFile();
            if (!string.IsNullOrEmpty(path)) _overrides.Text = path;
        };

        return Page("上書きリスト", Row(Caption("ファイル"), _overrides, browse));
    }

    private bool _fittingTabs;

    /// <summary>全タブの中身を測って、一番高いものに合わせてタブコントロールの高さを決める。</summary>
    private void FitTabsHeight(TabControl tabs)
    {
        if (_fittingTabs || !tabs.IsHandleCreated || tabs.TabPages.Count == 0) return;
        _fittingTabs = true;
        try
        {
            var area = tabs.DisplayRectangle;
            int chrome = tabs.Height - area.Height; // タブの見出しと枠の分
            int tallest = 0;
            foreach (TabPage page in tabs.TabPages)
            {
                if (page.Controls.Count == 0 || page.Controls[0] is not FlowLayoutPanel flow) continue;
                // ページの内側の余白を引いた幅で測る(広めに測ると折り返しを見落として下の行が隠れる)
                int width = Math.Max(area.Width - page.Padding.Horizontal, 1);
                int height = flow.GetPreferredSize(new Size(width, 0)).Height + page.Padding.Vertical;
                tallest = Math.Max(tallest, height);
            }

            int wanted = chrome + tallest + LogicalToDeviceUnits(6);
            tabs.MinimumSize = new Size(0, wanted);
            if (tabs.Height != wanted) tabs.Height = wanted;
        }
        finally
        {
            _fittingTabs = false;
        }
    }

    private static TabPage Page(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(8, 10, 8, 4),
        };
        foreach (var control in controls)
            control.Margin = new Padding(control.Margin.Left, control.Margin.Top, 18, control.Margin.Bottom);
        flow.Controls.AddRange(controls);

        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(flow);
        return page;
    }

    /// <summary>ラベルと入力欄を横に並べた1行。</summary>
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 4, 0) };

    private FlowLayoutPanel BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6),
        };
        // 「解析して確認」を主ボタン(塗りつぶし)、「この結果で実行」を副ボタン(白地に枠)、「中止」を目立たない色にする
        StyleButton(_analyzeButton, ButtonRole.Primary);
        StyleButton(_runButton, ButtonRole.Secondary);
        StyleButton(_cancelButton, ButtonRole.Quiet);
        panel.Controls.Add(_analyzeButton);
        panel.Controls.Add(_runButton);
        panel.Controls.Add(_cancelButton);
        return panel;
    }

    /// <summary>平らなボタンにする。色は役割とテーマで決まる(<see cref="Theme.ApplyButton"/>)。</summary>
    private static void StyleButton(Button button, ButtonRole role)
    {
        button.Tag = role;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(12, 5, 12, 5);
        button.Margin = new Padding(0, 0, 10, 0);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 1;
        button.EnabledChanged += (_, _) =>
        {
            Theme.ApplyButton(button);
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
        };
        Theme.ApplyButton(button);
    }

    // ---- ドラッグ&ドロップ -------------------------------------------------

    private void EnableDrop(Control target, Action<string> apply)
    {
        target.AllowDrop = true;
        target.DragEnter += (_, e) =>
            e.Effect = !_busy && TryGetDroppedPath(e.Data, out string _) ? DragDropEffects.Copy : DragDropEffects.None;
        target.DragDrop += (_, e) =>
        {
            if (!_busy && TryGetDroppedPath(e.Data, out string path)) apply(path);
        };
    }

    private static bool TryGetDroppedPath(IDataObject? data, out string path)
    {
        path = "";
        if (data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            path = files[0];
            return true;
        }
        return false;
    }

    /// <summary>入力フォルダを設定する。ファイルが落とされたときは、そのファイルのあるフォルダにする。</summary>
    private void SetInputFolder(string path)
    {
        string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        _input.Text = folder;
        if (string.IsNullOrWhiteSpace(_output.Text))
            _output.Text = Path.Combine(folder, "sorted");
    }

    // ---- 集計・グループ ----------------------------------------------------

    private void HideSummary()
    {
        foreach (var chip in _summary.Controls.Cast<Control>().ToList())
            chip.Dispose();
        _summary.Visible = false;
    }

    /// <summary>解析(や実行)の結果を、件数のラベルで一覧の上に出す。</summary>
    private void ShowSummary(SortPlan plan)
    {
        HideSummary();
        if (plan.Items.Count == 0) return;

        string[] special = { FolderNames.Unknown, FolderNames.NoMetadata, FolderNames.Multiple };
        int unknown = plan.Items.Count(i => i.Folders.Contains(FolderNames.Unknown));
        int noMetadata = plan.Items.Count(i => i.Folders.Contains(FolderNames.NoMetadata));
        int multiple = plan.Items.Count(i => i.Folders.Contains(FolderNames.Multiple));
        int characters = plan.Items.SelectMany(i => i.Folders)
            .Where(f => !special.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int skipped = plan.Items.Count - plan.TargetCount;
        int done = plan.Items.Count(i => i.Status is "コピー済" or "移動済");
        int failed = plan.Items.Count(i => i.Status.StartsWith("失敗", StringComparison.Ordinal));

        void Add(string text, ChipKind kind) => _summary.Controls.Add(Chip(text, kind));

        Add($"解析 {plan.Items.Count} 件", ChipKind.Gray);
        Add($"実行の対象 {plan.TargetCount} 件", ChipKind.Blue);
        if (skipped > 0) Add($"対象外 {skipped} 件", ChipKind.Gray);
        Add($"キャラクター {characters} 種", ChipKind.Teal);
        if (multiple > 0) Add($"複数キャラ {multiple} 件", ChipKind.Teal);
        if (unknown > 0) Add($"不明 {unknown} 件", ChipKind.Amber);
        if (noMetadata > 0) Add($"メタデータなし {noMetadata} 件", ChipKind.Amber);
        if (done > 0) Add($"完了 {done} 件", ChipKind.Green);
        if (failed > 0) Add($"失敗 {failed} 件", ChipKind.Red);
        _summary.Visible = true;
    }

    private static Label Chip(string text, ChipKind kind)
    {
        var chip = new Label
        {
            Text = text,
            AutoSize = true,
            Tag = kind, // テーマを切り替えたときに、色を当て直すため
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, 6, 4),
        };
        Theme.Apply(chip);
        return chip;
    }

    private ListViewGroup GroupFor(string name)
    {
        if (!_groups.TryGetValue(name, out var group))
        {
            group = NewGroup(name, 0);
            _groups[name] = group;
            _list.Groups.Add(group);
        }
        return group;
    }

    private static ListViewGroup NewGroup(string name, int count) =>
        new(name, count > 0 ? $"{name} ({count})" : name)
        {
            CollapsedState = ListViewGroupCollapsedState.Expanded, // 見出しをクリックして折りたためるようにする
        };

    private static string GroupName(SortItem item) => item.Folders.Count > 0 ? item.Folders[0] : "(仕分け先なし)";

    /// <summary>
    /// 行の仕分け先からグループを作り直し、名前順(_ で始まる特別なものは最後)に並べ、件数を見出しに出す。
    /// 解析の後、仕分け先を手で変えた後、グループ表示をオンに戻した後に呼ぶ。
    /// </summary>
    private void FinalizeGroups()
    {
        if (_rows.Count == 0) return;

        // グループの中身ではなく行の仕分け先から作るので、空になったグループは自然に消える
        var ordered = _rows
            .GroupBy(p => GroupName(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.StartsWith('_') ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Name: g.Key, Rows: g.ToList()))
            .ToList();

        _list.BeginUpdate();
        try
        {
            _list.Groups.Clear();
            _groups.Clear();
            foreach (var (name, rows) in ordered)
            {
                var group = NewGroup(name, rows.Count);
                _groups[name] = group;
                _list.Groups.Add(group);
                foreach (var row in rows) row.Group = group;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private static Font PickFont()
    {
        var installed = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { "Yu Gothic UI", "Meiryo UI", "Segoe UI" })
        {
            if (installed.Contains(name)) return new Font(name, 9.5f);
        }
        return SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
    }

    private string? BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog();
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private string? BrowseFile()
    {
        using var dialog = new OpenFileDialog { Filter = "テキスト/CSV|*.txt;*.csv|すべてのファイル|*.*" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    // ---- 解析と実行 --------------------------------------------------------

    private SortOptions CreateSortOptions() => new()
    {
        InputDir = _input.Text,
        OutputDir = _output.Text,
        Recursive = _recursive.Checked,
        Multi = _multi.SelectedIndex switch
        {
            1 => MultiMode.FirstOnly,
            2 => MultiMode.EachFolder,
            _ => MultiMode.MultipleFolder,
        },
        UseDanbooru = _useDanbooru.Checked,
        ResolveAliases = _aliases.Checked,
        UnderscoreFolderNames = _underscore.Checked,
        MinPostCount = (int)_minPosts.Value,
        OverridesPath = string.IsNullOrWhiteSpace(_overrides.Text) ? null : _overrides.Text,
        InferFromImage = _infer.Checked,
        InferUnknown = _inferUnknown.Checked,
        TaggerModelId = ModelCatalog.All[Math.Max(0, _model.SelectedIndex)].Id,
        CharacterThreshold = (double)_threshold.Value,
        UseGpu = _useGpu.Checked,
        IncludeUnknown = _includeUnknown.Checked,
        IncludeNoMetadata = _includeNoMetadata.Checked,
    };

    private Progress<SortProgress> CreateProgress() => new(p =>
    {
        _status.Text = $"{p.Phase}: {p.Done}/{p.Total}";
        _bar.Maximum = Math.Max(1, p.Total);
        _bar.Value = Math.Min(Math.Max(0, p.Done), _bar.Maximum);
    });

    /// <summary>「解析して確認」: 仕分け先を決めて一覧に出す。ファイルは変更せず、結果をメモリに保持する。</summary>
    private async Task AnalyzeAsync()
    {
        if (!Directory.Exists(_input.Text))
        {
            MessageBox.Show(this, "入力フォルダを選択してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_output.Text))
        {
            MessageBox.Show(this, "出力フォルダを選択してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var options = CreateSortOptions();

        _plan = null;
        ClearList();
        _stale = false;
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            if (options.InferFromImage && !await EnsureModelAsync(ModelCatalog.Find(options.TaggerModelId), _cts.Token))
            {
                _status.Text = "推定モデルを用意できなかったため、解析を開始しませんでした";
                return;
            }

            var plan = await SortService.PlanAsync(options, CreateProgress(), new Progress<SortItem>(AddRow), _cts.Token);
            _plan = plan;
            _status.Text = DescribePlan(plan);
            ShowSummary(plan);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "中止しました(途中までの解析結果は使えません。もう一度「解析して確認」を押してください)";
            _stale = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "エラーで停止しました";
        }
        finally
        {
            FinalizeGroups();
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private static string DescribePlan(SortPlan plan)
    {
        if (plan.Items.Count == 0)
            return "対象の画像が見つかりませんでした";

        int skipped = plan.Items.Count - plan.TargetCount;
        string text = $"{plan.Items.Count} 件を解析しました(実行の対象 {plan.TargetCount} 件"
                      + (skipped > 0 ? $"、対象外 {skipped} 件" : "")
                      + ")。";
        text += plan.TargetCount > 0
            ? "内容を確認して「この結果で実行」を押してください"
            : "実行の対象がないため、実行できません";
        if (plan.Notes.Count > 0) text += " / " + string.Join(" / ", plan.Notes);
        if (plan.Warning != null) text += " / 注意: " + plan.Warning;
        return text;
    }

    /// <summary>「この結果で実行」: 解析の結果のとおりにコピー/移動する。再解析はしない。</summary>
    private async Task ExecutePlanAsync()
    {
        var plan = _plan;
        if (plan == null || plan.Executed || plan.TargetCount == 0) return;

        var action = _action.SelectedIndex == 1 ? SortAction.Move : SortAction.Copy;
        if (action == SortAction.Move &&
            MessageBox.Show(
                this,
                $"{plan.TargetCount} 件の画像を、元の場所から出力フォルダへ移動します。よろしいですか?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        string verb = action == SortAction.Move ? "移動" : "コピー";

        try
        {
            var report = await SortService.ExecuteAsync(
                plan, action, CreateProgress(), new Progress<SortItem>(UpdateRow), _cts.Token);

            int failed = report.Items.Count(i => i.Status.StartsWith("失敗", StringComparison.Ordinal));
            _status.Text = $"{report.Items.Count - failed} 件を{verb}しました"
                           + (failed > 0 ? $"(失敗 {failed} 件。「状態」列を確認してください)" : "")
                           + " / 記録は出力フォルダの sort_log_日時.csv にあります"
                           + "。続けて仕分けるには、もう一度「解析して確認」を押してください";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "中止しました(一部はすでに処理済みです。「状態」列と sort_log_日時.csv を確認してください)";
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "エラーで停止しました";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (plan.Executed) ShowSummary(plan); // 完了・失敗の件数を足して表示する
            if (plan.Executed && ReferenceEquals(_plan, plan))
                _plan = null; // 実行した結果は、もう一度実行できない
            SetBusy(false);
        }
    }

    // ---- 解析結果の無効化・ボタンの状態 ------------------------------------

    private void Watch(params Control[] controls)
    {
        foreach (var control in controls)
        {
            switch (control)
            {
                case TextBox text: text.TextChanged += (_, _) => OnOptionChanged(); break;
                case CheckBox check: check.CheckedChanged += (_, _) => OnOptionChanged(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => OnOptionChanged(); break;
                case NumericUpDown number: number.ValueChanged += (_, _) => OnOptionChanged(); break;
            }
        }
    }

    /// <summary>設定が変わると、いまの解析結果は実行したい内容と合わなくなる。結果を無効にして再解析を促す。</summary>
    private void OnOptionChanged()
    {
        if (_plan == null) return;

        _plan = null;
        HideSummary();
        _stale = true;
        _list.Invalidate();
        _status.Text = "設定が変わったため、解析結果を無効にしました。もう一度「解析して確認」を押してください";
        UpdateButtons();
    }

    /// <summary>実行できるのは、処理中でなく、実行済みでない解析結果に対象の画像があるときだけ。</summary>
    private void UpdateButtons()
    {
        _analyzeButton.Enabled = !_busy;
        _cancelButton.Enabled = _busy;
        _runButton.Enabled = !_busy && _plan is { Executed: false } plan && plan.TargetCount > 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_pathPanel != null) _pathPanel.Enabled = !busy;
        if (_optionsPanel != null) _optionsPanel.Enabled = !busy;
        UpdateButtons();
        if (!busy) ApplySort(); // 処理中は行を足すたびに並べ替わらないよう、終わってからまとめて並べ替える
    }

    private void ClearList()
    {
        _list.ListViewItemSorter = null;
        _list.Items.Clear();
        _list.Groups.Clear();
        _groups.Clear();
        HideSummary();
        _rows.Clear();
        _thumbGeneration++;
        _thumbCache.Clear();
    }

    /// <summary>推定モデルが未取得なら、確認してからダウンロードする。使える状態になれば true。</summary>
    private async Task<bool> EnsureModelAsync(TaggerModel model, CancellationToken ct)
    {
        if (ModelCatalog.IsInstalled(model)) return true;

        string dir = ModelCatalog.DirectoryFor(model);
        var answer = MessageBox.Show(
            this,
            $"画像から推定するためのモデル「{model.DisplayName}」がまだありません。\n"
            + "Hugging Face からダウンロードします(数百MB以上あります)。\n\n"
            + $"保存先: {dir}\n\nダウンロードしますか?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return false;

        var progress = new Progress<DownloadProgress>(p =>
        {
            long doneMb = p.BytesDone / (1024 * 1024);
            if (p.BytesTotal is long total && total > 0)
            {
                _status.Text = $"モデルをダウンロード中: {p.FileName} ({doneMb} / {total / (1024 * 1024)} MB)";
                _bar.Maximum = 1000;
                _bar.Value = (int)Math.Min(1000, p.BytesDone * 1000 / total);
            }
            else
            {
                _status.Text = $"モデルをダウンロード中: {p.FileName} ({doneMb} MB)";
            }
        });

        try
        {
            await ModelDownloader.DownloadAsync(model, null, progress, ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"ダウンロードに失敗しました。\n{ex.Message}\n\n"
                + "手動で用意する場合は、次のフォルダに model.onnx と selected_tags.csv を置いてください。\n"
                + dir,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void AddRow(SortItem item)
    {
        var row = new ListViewItem(new[]
        {
            Path.GetFileName(item.Path),
            item.Source,
            CharactersText(item),
            FoldersText(item),
            item.Status,
        })
        {
            Tag = item, // サムネイルの読み込み元を引くために保持する
            Group = GroupFor(GroupName(item)),
        };
        _rows[item] = row;
        _list.Items.Add(row);
    }

    /// <summary>実行中に、処理した画像の「状態」を更新する。</summary>
    private void UpdateRow(SortItem item)
    {
        if (_rows.TryGetValue(item, out var row))
            row.SubItems[4].Text = item.Status;
    }

    /// <summary>キャラクターが決まらなかった画像は、推定の候補を確信度つきで出す。</summary>
    private static string CharactersText(SortItem item)
    {
        if (item.Characters.Count > 0 || item.CandidateCharacters.Count == 0)
            return string.Join(", ", item.Characters);
        return "候補: " + string.Join(", ", item.CandidateCharacters.Take(3).Select(c => $"{c.Tag} {c.Score:P0}"));
    }

    private static string FoldersText(SortItem item) =>
        string.Join(", ", item.Folders) + (item.IsManual ? "(手動)" : "");

    // ---- 仕分け先の手動指定 ------------------------------------------------

    /// <summary>仕分け先を手で変えられるのは、処理中でなく、実行前の解析結果があるときだけ。</summary>
    private bool CanEditPlan => !_busy && _plan is { Executed: false };

    private List<SortItem> SelectedSortItems() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(r => r.Tag).OfType<SortItem>().ToList();

    private ContextMenuStrip BuildListMenu()
    {
        const string candidateTag = "candidate";
        var menu = new ContextMenuStrip();
        var assign = new ToolStripMenuItem("仕分け先を指定(&A)...", null, (_, _) => AssignSelected())
        {
            ShortcutKeyDisplayString = "F2", // キー操作は一覧の KeyDown で受ける(メニューを開いていなくても効くように)
        };
        var reset = new ToolStripMenuItem("自動の仕分け先に戻す(&R)", null, (_, _) =>
        {
            var plan = _plan;
            var selected = SelectedSortItems();
            if (plan == null || !CanEditPlan || selected.Count == 0) return;
            plan.ResetToAutomatic(selected);
            RefreshRows(plan, selected);
            _status.Text = $"{selected.Count} 件を、解析で決まった仕分け先に戻しました";
        });
        var open = new ToolStripMenuItem("ファイルを開く(&O)", null, (_, _) =>
        {
            // 複数選んでいても、開くのはフォーカスのある1枚だけ (大量に開かないように)
            var focused = _list.FocusedItem is { Selected: true } f ? f : _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
            OpenFile(focused?.Tag as SortItem);
        })
        {
            ShortcutKeyDisplayString = "Enter", // ダブルクリックと Enter は一覧の ItemActivate で受ける
        };
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(assign);
        menu.Items.Add(reset);

        menu.Opening += (_, e) =>
        {
            // 候補のキャラクターは、選んでいる画像に合わせて毎回作り直す
            foreach (var old in menu.Items.Cast<ToolStripItem>().Where(i => Equals(i.Tag, candidateTag)).ToList())
            {
                menu.Items.Remove(old);
                old.Dispose();
            }

            var selected = SelectedSortItems();
            open.Enabled = selected.Count > 0; // 開くのは、解析結果が無効になっていても、実行の後でもできる
            bool editable = CanEditPlan && selected.Count > 0;
            assign.Enabled = editable;
            reset.Enabled = editable && selected.Any(i => i.IsManual);
            if (!editable) return;

            var candidates = selected
                .SelectMany(i => i.CandidateFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            int first = menu.Items.IndexOf(assign); // 「ファイルを開く」の下、「仕分け先を指定」の上に並べる
            int index = first;
            foreach (string folder in candidates)
            {
                menu.Items.Insert(index++, new ToolStripMenuItem($"候補: {folder}", null, (_, _) => Assign(selected, folder))
                {
                    Tag = candidateTag,
                });
            }
            if (index > first) menu.Items.Insert(index, new ToolStripSeparator { Tag = candidateTag });
        };
        return menu;
    }

    /// <summary>選んでいる画像の仕分け先を、ダイアログで選ぶか入力して決める。</summary>
    private void AssignSelected()
    {
        var plan = _plan;
        var selected = SelectedSortItems();
        if (plan == null || !CanEditPlan || selected.Count == 0) return;

        // 選んでいる画像の候補を先に、そのあとにこの結果に出てくるキャラクターを並べる
        var choices = selected
            .SelectMany(i => i.CandidateFolders)
            .Concat(plan.CharacterFolders())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string initial = selected.Count == 1
            ? selected[0].Folders.FirstOrDefault(f => !f.StartsWith('_')) ?? selected[0].CandidateFolders.FirstOrDefault() ?? ""
            : "";

        string? folder = AskFolder(selected.Count, choices, initial);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Assign(selected, folder);
    }

    private void Assign(List<SortItem> items, string folder)
    {
        var plan = _plan;
        if (plan == null || !CanEditPlan) return;

        string name = plan.AssignManually(items, folder);
        RefreshRows(plan, items);
        _status.Text = $"{items.Count} 件の仕分け先を「{name}」にしました(「この結果で実行」で反映されます)";
    }

    /// <summary>仕分け先を変えた行の表示・グループ・集計・ボタンを更新する。</summary>
    private void RefreshRows(SortPlan plan, List<SortItem> items)
    {
        foreach (var item in items)
        {
            if (!_rows.TryGetValue(item, out var row)) continue;
            row.SubItems[3].Text = FoldersText(item);
            row.SubItems[4].Text = item.Status;
        }
        FinalizeGroups();
        ApplySort();
        ShowSummary(plan);
        UpdateButtons();
        _list.Invalidate();
    }

    // ---- 並べ替え・ファイルを開く ------------------------------------------

    /// <summary>見出しをクリックすると、その列で昇順に並べる。同じ列をもう一度クリックすると降順にする。</summary>
    private void OnColumnClick(int column)
    {
        _sortDescending = column == _sortColumn && !_sortDescending;
        _sortColumn = column;
        for (int i = 0; i < _list.Columns.Count; i++)
            _list.Columns[i].Text = ColumnTitles[i] + (i == column ? (_sortDescending ? " ▼" : " ▲") : "");
        if (!_busy) ApplySort();
    }

    private void ApplySort()
    {
        if (_sortColumn < 0 || _list.Items.Count == 0) return;
        _list.ListViewItemSorter = new ListViewColumnSorter(_sortColumn, _sortDescending); // 設定すると並べ替わる
    }

    /// <summary>画像を、Windows でその種類に関連付けられたアプリで開く。移動した画像は移動先を開く。</summary>
    private void OpenFile(SortItem? item)
    {
        if (item == null) return;

        string path = CurrentFilePath(item);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"ファイルが見つかりません。\n{path}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            // 関連付けられたアプリがないときなど
            MessageBox.Show(this, $"ファイルを開けませんでした。\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? AskFolder(int count, List<string> choices, string initial)
    {
        using var dialog = new Form
        {
            Text = "仕分け先を指定",
            Font = Font,
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 12, 14, 12),
        };
        var box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = LogicalToDeviceUnits(380),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Margin = new Padding(0, 6, 0, 12),
        };
        box.Items.AddRange(choices.Cast<object>().ToArray());
        box.Text = initial;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel };
        StyleButton(ok, ButtonRole.Primary);
        StyleButton(cancel, ButtonRole.Quiet);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = $"選んだ {count} 件の仕分け先にするフォルダ名を、一覧から選ぶか入力してください。",
            AutoSize = true,
        });
        layout.Controls.Add(box);
        layout.Controls.Add(buttons);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        Theme.Apply(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }
}
