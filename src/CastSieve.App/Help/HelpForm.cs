using System.Reflection;

namespace CastSieve.App;

/// <summary>埋め込みのマニュアル (Help/manual.html) を表示するウィンドウ。</summary>
public sealed class HelpForm : Form
{
    // WebBrowser (IE のエンジン) は prefers-color-scheme を使えないので、ダークのときはこの CSS を足して表示する
    private const string DarkCss = """
        <style>
          html, body { background: #1e1e1e; color: #e5e7eb; }
          html {
            scrollbar-face-color: #3a3a3a; scrollbar-track-color: #1e1e1e; scrollbar-arrow-color: #a0a0a0;
            scrollbar-shadow-color: #3a3a3a; scrollbar-highlight-color: #3a3a3a;
            scrollbar-3dlight-color: #1e1e1e; scrollbar-darkshadow-color: #1e1e1e;
            -ms-scrollbar-face-color: #3a3a3a; -ms-scrollbar-track-color: #1e1e1e; -ms-scrollbar-arrow-color: #a0a0a0;
            -ms-scrollbar-shadow-color: #3a3a3a; -ms-scrollbar-highlight-color: #3a3a3a;
            -ms-scrollbar-3dlight-color: #1e1e1e; -ms-scrollbar-darkshadow-color: #1e1e1e;
          }
          h2 { border-bottom-color: #5b7bd5; }
          .lead, .stage span { color: #a1a1aa; }
          code, pre { background: #2d2d30; }
          th, td { border-color: #3f3f46; }
          th { background: #2a2f3a; }
          .stage { background: #252526; border-color: #52525b; }
          .stage.main { background: #1b2a41; border-color: #3b82f6; }
          .arrow { color: #9ca3af; }
          .note { background: #3a2f12; }
          a, .toc a { color: #7aa7ff; }
        </style>
        """;

    private readonly WebBrowser _browser = new()
    {
        Dock = DockStyle.Fill,
        IsWebBrowserContextMenuEnabled = false,
        AllowWebBrowserDrop = false,
        ScriptErrorsSuppressed = true,
    };

    public HelpForm()
    {
        Text = "CastSieve - 使い方";
        Width = 960;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Controls.Add(_browser);

        Load += (_, _) => ApplyTheme();
    }

    /// <summary>いまのテーマでマニュアルを表示し直す。</summary>
    public void ApplyTheme()
    {
        Theme.Apply(this);
        string html = ReadManual();
        if (Theme.Current.IsDark) html = html.Replace("</head>", DarkCss + "</head>", StringComparison.OrdinalIgnoreCase);
        _browser.DocumentText = html;
    }

    private static string ReadManual()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("manual.html", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return "<html><body>マニュアルが見つかりませんでした。</body></html>";

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
