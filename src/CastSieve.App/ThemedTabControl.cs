namespace CastSieve.App;

/// <summary>
/// ダークのときは見出しと枠を自分で描く TabControl。標準の TabControl は色を変えられないため。
/// ライトのときは標準の描画のまま。
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
    private Palette? _dark;

    public void SetDarkPalette(Palette? palette)
    {
        if (_dark == palette) return;
        _dark = palette;
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            palette != null);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_dark == null || TabCount == 0)
        {
            base.OnPaint(e);
            return;
        }

        var p = _dark;
        var g = e.Graphics;
        g.Clear(p.Window);

        // タブの中(ページの周り)
        int top = GetTabRect(0).Bottom;
        var body = Rectangle.FromLTRB(0, top, Width - 1, Height - 1);
        using (var fill = new SolidBrush(p.Panel))
            g.FillRectangle(fill, body);
        using var border = new Pen(p.Border);
        g.DrawRectangle(border, body);

        for (int i = 0; i < TabCount; i++)
        {
            var tab = GetTabRect(i);
            bool selected = i == SelectedIndex;
            if (selected)
            {
                // 選んでいるタブは少し高くして、下の枠線を消し、中とつながって見えるようにする
                tab = Rectangle.FromLTRB(tab.Left, tab.Top - 2, tab.Right, tab.Bottom + 1);
                using (var fill = new SolidBrush(p.Panel))
                    g.FillRectangle(fill, tab);
                g.DrawLine(border, tab.Left, tab.Bottom - 1, tab.Left, tab.Top);
                g.DrawLine(border, tab.Left, tab.Top, tab.Right - 1, tab.Top);
                g.DrawLine(border, tab.Right - 1, tab.Top, tab.Right - 1, tab.Bottom - 1);
                using var accent = new Pen(p.Accent, 2);
                g.DrawLine(accent, tab.Left + 1, tab.Top + 1, tab.Right - 2, tab.Top + 1);
            }

            Color text = !Enabled ? p.GrayText : selected ? p.Text : p.SubText;
            TextRenderer.DrawText(
                g,
                TabPages[i].Text,
                Font,
                tab,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
