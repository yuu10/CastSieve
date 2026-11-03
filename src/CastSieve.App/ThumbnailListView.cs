using System.Runtime.InteropServices;

namespace CastSieve.App;

/// <summary>サムネイルを描くときのちらつきを抑えるため、ダブルバッファを有効にした ListView。</summary>
internal sealed class ThumbnailListView : ListView
{
    private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E;
    private const int NM_CUSTOMDRAW = -12;
    private const int CDDS_PREPAINT = 0x1;
    private const int CDDS_POSTPAINT = 0x2;
    private const int CDRF_NOTIFYPOSTPAINT = 0x10;
    private const int LVCDI_GROUP = 1;
    private const int LVM_GETGROUPINFO = 0x1000 + 149;
    private const int LVGF_HEADER = 0x1;

    public ThumbnailListView()
    {
        DoubleBuffered = true;
    }

    /// <summary>グループ見出しの地と文字の色。null なら Windows のテーマのまま描く。</summary>
    public (Color Back, Color Fore)? GroupHeaderColors { get; set; }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_REFLECT_NOTIFY || GroupHeaderColors is not var (back, fore)) return;

        var header = Marshal.PtrToStructure<NMHDR>(m.LParam);
        if (header.code != NM_CUSTOMDRAW) return;
        var draw = Marshal.PtrToStructure<NMLVCUSTOMDRAW>(m.LParam);
        if (draw.dwItemType != LVCDI_GROUP) return;

        // ダークのテーマではグループ見出しの文字が紺色で読みにくい(文字色の指定も効かない)。
        // 標準の描画(折りたたみの矢印を含む)の後に、見出しの文字だけを明るい色で描き直す
        if (draw.dwDrawStage == CDDS_PREPAINT)
        {
            m.Result = (IntPtr)((int)m.Result | CDRF_NOTIFYPOSTPAINT);
        }
        else if (draw.dwDrawStage == CDDS_POSTPAINT)
        {
            // rc はグループ全体(見出しと行)、rcText が見出しの文字の場所
            var textRect = Rectangle.FromLTRB(draw.rcText.left, draw.rcText.top, draw.rcText.right, draw.rcText.bottom);
            string? text = GroupHeaderText((int)draw.dwItemSpec);
            if (text == null || textRect.Width <= 0 || textRect.Height <= 0) return;

            // 右端の折りたたみの矢印は残したいので、塗り直すのは文字の幅だけ
            const TextFormatFlags flags =
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            using var g = Graphics.FromHdc(draw.hdc);
            int inset = LogicalToDeviceUnits(6); // 標準の描画と同じ位置に文字を置く
            int width = TextRenderer.MeasureText(g, text, Font, textRect.Size, flags).Width;
            var area = new Rectangle(textRect.Left, textRect.Top, Math.Min(textRect.Width, inset + width + inset), textRect.Height);
            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, area);
            TextRenderer.DrawText(g, text, Font, Rectangle.FromLTRB(area.Left + inset, area.Top, area.Right, area.Bottom), fore, flags);
        }
    }

    private string? GroupHeaderText(int groupId)
    {
        const int capacity = 260;
        IntPtr buffer = Marshal.AllocHGlobal(capacity * 2);
        try
        {
            var group = new LVGROUP
            {
                cbSize = (uint)Marshal.SizeOf<LVGROUP>(),
                mask = LVGF_HEADER,
                pszHeader = buffer,
                cchHeader = capacity,
            };
            if (SendMessage(Handle, LVM_GETGROUPINFO, (IntPtr)groupId, ref group) == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref LVGROUP lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NMHDR
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMLVCUSTOMDRAW
    {
        public NMHDR hdr;
        public int dwDrawStage;
        public IntPtr hdc;
        public RECT rc;
        public IntPtr dwItemSpec;
        public uint uItemState;
        public IntPtr lItemlParam;
        public int clrText;
        public int clrTextBk;
        public int iSubItem;
        public int dwItemType;
        public int clrFace;
        public int iIconEffect;
        public int iIconPhase;
        public int iPartId;
        public int iStateId;
        public RECT rcText;
        public uint uAlign;
    }

    // Windows XP の形(見出しだけを読むので、これで足りる)
    [StructLayout(LayoutKind.Sequential)]
    private struct LVGROUP
    {
        public uint cbSize;
        public uint mask;
        public IntPtr pszHeader;
        public int cchHeader;
        public IntPtr pszFooter;
        public int cchFooter;
        public int iGroupId;
        public uint stateMask;
        public uint state;
        public uint uAlign;
    }
}
