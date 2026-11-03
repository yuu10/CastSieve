using System.Collections;
using System.Runtime.InteropServices;

namespace CastSieve.App;

/// <summary>
/// 一覧の1つの列の文字で並べ替える。エクスプローラーと同じく、数字は数の大きさで比べる (img2 → img10 の順)。
/// 同じ値どうしは、ファイル名で並べる。
/// </summary>
internal sealed class ListViewColumnSorter : IComparer
{
    public int Column { get; }
    public bool Descending { get; }

    public ListViewColumnSorter(int column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem a || y is not ListViewItem b) return 0;

        int result = StrCmpLogicalW(Text(a, Column), Text(b, Column));
        if (result == 0 && Column != 0) result = StrCmpLogicalW(Text(a, 0), Text(b, 0));
        return Descending ? -result : result;
    }

    private static string Text(ListViewItem item, int column) =>
        column < item.SubItems.Count ? item.SubItems[column].Text : "";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);
}
