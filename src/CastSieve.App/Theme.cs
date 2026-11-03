using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CastSieve.App;

/// <summary>テーマの選び方。System は Windows の「アプリ モード」(ライト/ダーク)に合わせる。</summary>
internal enum ThemeMode { System, Light, Dark }

/// <summary>ボタンの役割。色はテーマごとに <see cref="Palette"/> から決める。</summary>
internal enum ButtonRole { Primary, Secondary, Quiet }

/// <summary>集計ラベルの色の種類。</summary>
internal enum ChipKind { Gray, Blue, Teal, Amber, Green, Red }

internal readonly record struct ButtonColors(Color Back, Color Fore, Color Border);

internal readonly record struct ChipColors(Color Back, Color Fore);

/// <summary>画面で使う色の一式。</summary>
internal sealed record Palette
{
    public bool IsDark { get; init; }
    public Color Window { get; init; }       // フォームの地
    public Color Panel { get; init; }        // タブの中
    public Color Input { get; init; }        // 入力欄
    public Color ListBack { get; init; }
    public Color Zebra { get; init; }        // 一覧の1行おきの色
    public Color HeaderBack { get; init; }   // 一覧の見出し(ダークのときだけ自分で描く)
    public Color Border { get; init; }
    public Color Text { get; init; }
    public Color SubText { get; init; }      // 補足の説明
    public Color GrayText { get; init; }     // 対象外・無効になった結果
    public Color Selection { get; init; }
    public Color SelectionText { get; init; }
    public Color Accent { get; init; }
    public Color StatusDone { get; init; }
    public Color StatusFailed { get; init; }
    public Color ThumbFill { get; init; }
    public Color ThumbBorder { get; init; }
    public ButtonColors Primary { get; init; }
    public ButtonColors Secondary { get; init; }
    public ButtonColors Quiet { get; init; }
    public ButtonColors Disabled { get; init; }
    public IReadOnlyDictionary<ChipKind, ChipColors> Chips { get; init; } = new Dictionary<ChipKind, ChipColors>();

    public ButtonColors For(ButtonRole role) => role switch
    {
        ButtonRole.Primary => Primary,
        ButtonRole.Secondary => Secondary,
        _ => Quiet,
    };

    public static readonly Palette Light = new()
    {
        IsDark = false,
        Window = SystemColors.Control,
        Panel = SystemColors.Window,
        Input = SystemColors.Window,
        ListBack = SystemColors.Window,
        Zebra = Color.FromArgb(246, 248, 251),
        HeaderBack = SystemColors.Control,
        Border = Color.FromArgb(200, 205, 212),
        Text = SystemColors.ControlText,
        SubText = Color.FromArgb(100, 105, 112),
        GrayText = SystemColors.GrayText,
        Selection = SystemColors.Highlight,
        SelectionText = SystemColors.HighlightText,
        Accent = Color.FromArgb(0, 103, 192),
        StatusDone = Color.FromArgb(22, 128, 57),
        StatusFailed = Color.FromArgb(200, 40, 40),
        ThumbFill = Color.FromArgb(235, 238, 242),
        ThumbBorder = Color.FromArgb(200, 205, 212),
        Primary = new(Color.FromArgb(0, 103, 192), Color.White, Color.FromArgb(0, 103, 192)),
        Secondary = new(Color.White, Color.FromArgb(0, 103, 192), Color.FromArgb(0, 103, 192)),
        Quiet = new(Color.FromArgb(243, 244, 246), Color.FromArgb(70, 75, 85), Color.FromArgb(205, 208, 214)),
        Disabled = new(Color.FromArgb(238, 240, 243), Color.FromArgb(150, 155, 162), Color.FromArgb(220, 223, 228)),
        Chips = new Dictionary<ChipKind, ChipColors>
        {
            [ChipKind.Gray] = new(Color.FromArgb(235, 237, 240), Color.FromArgb(90, 95, 105)),
            [ChipKind.Blue] = new(Color.FromArgb(225, 238, 251), Color.FromArgb(0, 70, 140)),
            [ChipKind.Teal] = new(Color.FromArgb(222, 241, 243), Color.FromArgb(20, 90, 100)),
            [ChipKind.Amber] = new(Color.FromArgb(255, 243, 214), Color.FromArgb(125, 85, 0)),
            [ChipKind.Green] = new(Color.FromArgb(222, 243, 229), Color.FromArgb(20, 100, 45)),
            [ChipKind.Red] = new(Color.FromArgb(252, 229, 229), Color.FromArgb(160, 30, 30)),
        },
    };

    public static readonly Palette Dark = new()
    {
        IsDark = true,
        Window = Color.FromArgb(32, 32, 32),
        Panel = Color.FromArgb(43, 43, 43),
        Input = Color.FromArgb(56, 56, 56),
        ListBack = Color.FromArgb(28, 28, 28),
        Zebra = Color.FromArgb(37, 37, 38),
        HeaderBack = Color.FromArgb(45, 45, 45),
        Border = Color.FromArgb(70, 70, 70),
        Text = Color.FromArgb(232, 232, 232),
        SubText = Color.FromArgb(165, 165, 165),
        GrayText = Color.FromArgb(125, 125, 125),
        Selection = Color.FromArgb(0, 84, 153),
        SelectionText = Color.White,
        Accent = Color.FromArgb(96, 170, 240),
        StatusDone = Color.FromArgb(100, 200, 130),
        StatusFailed = Color.FromArgb(240, 110, 110),
        ThumbFill = Color.FromArgb(52, 52, 52),
        ThumbBorder = Color.FromArgb(80, 80, 80),
        Primary = new(Color.FromArgb(0, 103, 192), Color.White, Color.FromArgb(0, 103, 192)),
        Secondary = new(Color.FromArgb(43, 43, 43), Color.FromArgb(96, 170, 240), Color.FromArgb(96, 170, 240)),
        Quiet = new(Color.FromArgb(55, 55, 55), Color.FromArgb(215, 215, 215), Color.FromArgb(85, 85, 85)),
        Disabled = new(Color.FromArgb(45, 45, 45), Color.FromArgb(110, 110, 110), Color.FromArgb(60, 60, 60)),
        Chips = new Dictionary<ChipKind, ChipColors>
        {
            [ChipKind.Gray] = new(Color.FromArgb(55, 55, 58), Color.FromArgb(200, 200, 205)),
            [ChipKind.Blue] = new(Color.FromArgb(20, 50, 85), Color.FromArgb(150, 200, 250)),
            [ChipKind.Teal] = new(Color.FromArgb(20, 60, 65), Color.FromArgb(130, 210, 215)),
            [ChipKind.Amber] = new(Color.FromArgb(75, 55, 10), Color.FromArgb(245, 200, 110)),
            [ChipKind.Green] = new(Color.FromArgb(25, 65, 40), Color.FromArgb(140, 215, 160)),
            [ChipKind.Red] = new(Color.FromArgb(85, 30, 30), Color.FromArgb(245, 150, 150)),
        },
    };
}

/// <summary>
/// テーマ(ライト/ダーク)を画面に当てる。WinForms (.NET 8) には標準のダークモードがないので、
/// 色は自分で設定し、タイトルバー・スクロールバー・ドロップダウンは Windows のダーク用テーマを使う。
/// </summary>
internal static class Theme
{
    /// <summary>補足の説明に使うラベルの Tag。テーマを当てるときに薄い文字色にする。</summary>
    public static readonly object SubTextTag = new();

    public static Palette Current { get; private set; } = Palette.Light;

    public static Palette Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => Palette.Dark,
        ThemeMode.Light => Palette.Light,
        _ => SystemUsesDark() ? Palette.Dark : Palette.Light,
    };

    /// <summary>Windows の「既定のアプリ モード」がダークかどうか。</summary>
    public static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>以後に描くメニューや、Apply で当てる色を切り替える。</summary>
    public static void Use(Palette palette)
    {
        Current = palette;
        ToolStripManager.Renderer = palette.IsDark ? new DarkToolStripRenderer(palette) : new ToolStripProfessionalRenderer();
    }

    /// <summary>コントロールとその中身に、いまのテーマの色を当てる。</summary>
    public static void Apply(Control control)
    {
        var p = Current;
        switch (control)
        {
            case Form form:
                SetColors(form, p.Window, p.Text);
                OnHandle(form);
                break;
            case Button { Tag: ButtonRole } button:
                ApplyButton(button);
                return;
            case Button button:
                // 「参照...」など、役割を決めていないボタン。ダークのときだけ平らにして色を付ける
                if (p.IsDark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = p.Quiet.Back;
                    button.ForeColor = p.Quiet.Fore;
                    button.FlatAppearance.BorderColor = p.Quiet.Border;
                    button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(p.Quiet.Back, 0.2f);
                    button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(p.Quiet.Back, 0.05f);
                }
                else
                {
                    button.FlatStyle = FlatStyle.Standard;
                    button.ResetBackColor();
                    button.ResetForeColor();
                    button.UseVisualStyleBackColor = true;
                }
                return;
            case CheckBox check:
                // 標準の四角は白いままなので、ダークのときは平らな描き方にする
                check.FlatStyle = p.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                return;
            case Label { Tag: ChipKind kind } chip:
                chip.BackColor = p.Chips[kind].Back;
                chip.ForeColor = p.Chips[kind].Fore;
                return;
            case Label label when ReferenceEquals(label.Tag, SubTextTag):
                label.ForeColor = p.SubText;
                return;
            case TextBoxBase or NumericUpDown or ComboBox:
                SetColors(control, p.Input, p.Text);
                OnHandle(control);
                return; // 中の部品(NumericUpDown のボタンなど)には触らない
            case ListView list:
                SetColors(list, p.ListBack, p.Text);
                if (list is ThumbnailListView thumbs) thumbs.GroupHeaderColors = p.IsDark ? (p.ListBack, p.Accent) : null;
                OnHandle(list);
                list.Invalidate();
                return;
            case ProgressBar bar:
                OnHandle(bar);
                return;
            case ThemedTabControl tabs:
                tabs.SetDarkPalette(p.IsDark ? p : null);
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = !p.IsDark;
                if (p.IsDark) page.BackColor = p.Panel;
                else page.ResetBackColor();
                break;
            case ToolStrip:
                control.Invalidate();
                return;
        }

        foreach (Control child in control.Controls)
            Apply(child);
    }

    /// <summary>ライトのときは既定の色に戻す(システムの色の変更にも追従するように)。</summary>
    private static void SetColors(Control control, Color back, Color fore)
    {
        if (Current.IsDark)
        {
            control.BackColor = back;
            control.ForeColor = fore;
        }
        else
        {
            control.ResetBackColor();
            control.ResetForeColor();
        }
    }

    /// <summary>役割(Tag の <see cref="ButtonRole"/>)と、押せるかどうかに合わせてボタンの色を決める。</summary>
    public static void ApplyButton(Button button)
    {
        var p = Current;
        var colors = button.Tag is ButtonRole role ? p.For(role) : p.Quiet;
        var shown = button.Enabled ? colors : p.Disabled; // 押せないボタンは、色を薄くして見分けがつくようにする
        button.BackColor = shown.Back;
        button.ForeColor = shown.Fore;
        button.FlatAppearance.BorderColor = shown.Border;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(colors.Back, 0.2f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(colors.Back, 0.05f);
    }

    // ---- Windows のダーク用テーマ ------------------------------------------

    private static readonly ConditionalWeakTable<Control, object> Hooked = new();

    /// <summary>
    /// ウィンドウハンドルが要る設定(タイトルバー、スクロールバーなど)を当てる。
    /// まだハンドルがないときや、作り直されたときにも当たるよう、HandleCreated で毎回当て直す。
    /// </summary>
    private static void OnHandle(Control control)
    {
        if (!Hooked.TryGetValue(control, out _))
        {
            Hooked.Add(control, new object());
            control.HandleCreated += (_, _) => ApplyNative(control);
        }
        if (control.IsHandleCreated) ApplyNative(control);
    }

    private static void ApplyNative(Control control)
    {
        bool dark = Current.IsDark;
        switch (control)
        {
            case Form form:
                int on = dark ? 1 : 0;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1 以降)。古い Windows では失敗するだけ
                _ = DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
                break;
            case ListView list:
                SetTheme(list.Handle, dark ? "DarkMode_Explorer" : null);
                IntPtr header = SendMessage(list.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero) SetTheme(header, dark ? "DarkMode_ItemsView" : null);
                break;
            case ProgressBar bar:
                // テーマを外すと、ForeColor/BackColor で色を付けられる
                SetTheme(bar.Handle, dark ? "" : null, dark ? "" : null);
                bar.ForeColor = dark ? Current.Accent : SystemColors.Highlight;
                bar.BackColor = dark ? Current.Input : SystemColors.Control;
                break;
            case NumericUpDown number:
                foreach (Control part in number.Controls)
                {
                    if (part is TextBoxBase edit) SetTheme(edit.Handle, dark ? "DarkMode_CFD" : null);
                }
                break;
            default:
                // TextBox と ComboBox。枠・スクロールバー・ドロップダウンの矢印が暗くなる
                SetTheme(control.Handle, dark ? "DarkMode_CFD" : null);
                break;
        }
        control.Invalidate(true);
    }

    private static void SetTheme(IntPtr handle, string? appName, string? idList = null)
    {
        try
        {
            _ = SetWindowTheme(handle, appName, idList);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // テーマを使えない環境では、色を付けただけの見た目になる
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}

/// <summary>ダークのときのメニュー(メニューバー、右クリックメニュー)の描き方。</summary>
internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    private readonly Palette _palette;

    public DarkToolStripRenderer(Palette palette) : base(new DarkColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _palette.Text : _palette.GrayText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled != false ? _palette.Text : _palette.GrayText;
        base.OnRenderArrow(e);
    }

    /// <summary>標準のチェックの印は黒くて見えないので、明るい線で描く。</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var box = Rectangle.Inflate(e.ImageRectangle, 2, 2);
        using (var back = new SolidBrush(Color.FromArgb(0, 84, 153)))
            e.Graphics.FillRectangle(back, box);

        var old = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.White, Math.Max(1.5f, box.Height / 9f));
        var r = e.ImageRectangle;
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
            new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.28f),
        });
        e.Graphics.SmoothingMode = old;
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private readonly Color _back;
        private readonly Color _hover;
        private readonly Color _border;

        public DarkColorTable(Palette palette)
        {
            _back = palette.Panel;
            _hover = Color.FromArgb(65, 65, 65);
            _border = palette.Border;
            UseSystemColors = false;
        }

        public override Color MenuStripGradientBegin => _back;
        public override Color MenuStripGradientEnd => _back;
        public override Color ToolStripDropDownBackground => _back;
        public override Color ImageMarginGradientBegin => _back;
        public override Color ImageMarginGradientMiddle => _back;
        public override Color ImageMarginGradientEnd => _back;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color MenuItemPressedGradientBegin => _hover;
        public override Color MenuItemPressedGradientMiddle => _hover;
        public override Color MenuItemPressedGradientEnd => _hover;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _back;
        public override Color CheckBackground => _hover;
        public override Color CheckSelectedBackground => _hover;
        public override Color CheckPressedBackground => _hover;
    }
}
