// レポートの画像(仕様書 FR-60・§9.20。PadLab の ReportCard を SkiaSharp でなく WPF で描き直した)。残した結果の数・判定・外周の形などの図を 1 枚にまとめて PNG にする。
// 履歴のページの見本と書き出す画像は同じ Build で作る。色はメインの画面のテーマ、図はスティック診断の図と同じ描き方(パネルの地・灰の円・橙の線)。
// 載せるのは記録が持つ物(テスト・日時・パッドの名前と種類・数・判定・版)だけで、ユーザー名やパスは載せない。
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PadSight.Core;

namespace PadSight.App.Main.Diag;

public static class ReportCard
{
    /// <summary>書き出す画像の幅(論理の値。2 倍の細かさで描くので、画像は 1520 px)。</summary>
    public const double ExportWidth = 760;

    /// <summary>1 枚にまとめられる結果の数(画像が縦に長くなりすぎないように)。</summary>
    public const int MaxRecords = 10;

    const double Scale = 2;

    /// <summary>既定のファイル名。時刻は結果の日時(何件かなら一番新しい物)。</summary>
    public static string FileName(IReadOnlyList<DiagRecord> records) =>
        $"PadSight_診断_{records.Max(r => r.AtUtc).ToLocalTime():yyyyMMdd_HHmmss}.png";

    /// <summary>画像にして書く。書けなければ IOException・UnauthorizedAccessException を投げる。</summary>
    public static void SavePng(IReadOnlyList<DiagRecord> records, string path)
    {
        var e = Build(records);
        e.Width = ExportWidth;
        e.Measure(new Size(ExportWidth, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * Scale), (int)Math.Ceiling(e.ActualHeight * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }

    /// <summary>レポートの 1 枚(幅は呼ぶ側で決める)。窓の外でも描けるように、書体と文字の色をここで決める。</summary>
    public static Border Build(IReadOnlyList<DiagRecord> records)
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var versions = records.Select(r => r.AppVersion).Where(v => v.Length > 0).Distinct().ToList();
        var ver = Weak(versions.Count == 0 ? "PadSight" : "PadSight " + string.Join("・", versions));
        ver.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(ver, Dock.Right);
        head.Children.Add(ver);
        var title = Text("PadSight 診断レポート", 22, FontWeights.SemiBold);
        head.Children.Add(title);
        stack.Children.Add(head);
        foreach (var r in records.Take(MaxRecords))
            stack.Children.Add(Section(r));
        var note = Weak("値は SDL を通して測った参考値です。故障と決めるものではありません");
        note.Margin = new Thickness(2, 4, 0, 0);
        stack.Children.Add(note);

        var root = new Border { Child = stack, Padding = new Thickness(28, 24, 28, 24), CornerRadius = new CornerRadius(8) };
        root.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        TextElement.SetFontFamily(root, Panels.Draw.Ui);
        TextElement.SetFontSize(root, 14);
        root.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        return root;
    }

    /// <summary>結果 1 件の箱: 題と日時、パッド、図と数の札、判定。</summary>
    static Border Section(DiagRecord r)
    {
        var s = new StackPanel();
        var top = new DockPanel();
        var when = Weak(r.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        when.Margin = new Thickness(16, 0, 0, 0);
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        DockPanel.SetDock(when, Dock.Right);
        top.Children.Add(when);
        var t = Text(r.Title.Length > 0 ? r.Title : r.Kind, 18, FontWeights.SemiBold);
        t.TextWrapping = TextWrapping.Wrap;
        top.Children.Add(t);
        s.Children.Add(top);
        string pad = string.Join("  ・  ", new[] { r.PadName, r.PadKind }.Where(x => x.Length > 0));
        if (pad.Length > 0)
        {
            var p = Weak(pad);
            p.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(p);
        }

        var body = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        if (Figure(r) is { } fig)
        {
            fig.Margin = new Thickness(0, 0, 16, 8);
            body.Children.Add(fig);
        }
        var tiles = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
        foreach (var kv in r.Values)
            tiles.Children.Add(Tile(kv.Key, Number(kv.Value)));
        if (r.Values.Count == 0)
            tiles.Children.Add(Weak("数は残っていません"));
        Grid.SetColumn(tiles, 1);
        body.Children.Add(tiles);
        s.Children.Add(body);

        if (r.Verdict.Length > 0)
        {
            var v = Text(r.Verdict);
            v.TextWrapping = TextWrapping.Wrap;
            var vb = new Border { Child = v, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 4, 0, 4), Margin = new Thickness(0, 4, 0, 0) };
            vb.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            s.Children.Add(vb);
        }

        var b = new Border { Child = s, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 12) };
        b.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return b;
    }

    static Border Tile(string label, string value)
    {
        var l = Weak(label);
        l.FontSize = 12.5;
        l.Margin = new Thickness(0);
        l.MaxWidth = 220;
        var v = Text(value, 20, FontWeights.SemiBold);
        Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
        var st = new StackPanel();
        st.Children.Add(l);
        st.Children.Add(v);
        var b = new Border { Child = st, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 16, 10), Margin = new Thickness(0, 0, 8, 8), MinWidth = 120 };
        b.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        return b;
    }

    /// <summary>数の見せ方。整数はそのまま、小さい数ほど桁を多く(0.123・1.23・12.3)。</summary>
    public static string Number(double v)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!double.IsFinite(v))
            return "—";
        if (v == Math.Round(v) && Math.Abs(v) < 1e9)
            return v.ToString("#,0", inv);
        double a = Math.Abs(v);
        return v.ToString(a >= 100 ? "#,0.#" : a >= 1 ? "0.##" : "0.###", inv);
    }

    /// <summary>形の図。外周(stick_outer)は円の上の線、ほかの種類は左から右への線。描ける形が無ければ null。</summary>
    public static Border? Figure(DiagRecord r, double size = 180)
    {
        var shape = r.Shape;
        if (r.Kind == "stick_outer")
            return shape.Count >= 3 && shape.Any(Has) ? Ui.Picture(Polar(shape, size)) : null;
        return shape.Count >= 2 && shape.Any(double.IsFinite) ? Ui.Picture(Curve(shape, size * 4 / 3, size * 3 / 4)) : null;
    }

    /// <summary>外周の区切りに値があるか(0 以下・数でない物は「その向きは測れていない」)。</summary>
    static bool Has(double v) => double.IsFinite(v) && v > 0;

    /// <summary>区切りは 0°(右)から反時計回りに等しい角度で並ぶ。測れていない区切りで線を切る。</summary>
    static Canvas Polar(IReadOnlyList<double> shape, double size)
    {
        double c = size / 2, rad = c - 10;
        var cv = Board(size, size);
        cv.Children.Add(Place(new Ellipse { Width = rad * 2, Height = rad * 2, Stroke = Brushes.Gray, StrokeThickness = 1 }, c - rad, c - rad));
        cv.Children.Add(new Line { X1 = 4, X2 = size - 4, Y1 = c, Y2 = c, Stroke = Brushes.DimGray });
        cv.Children.Add(new Line { X1 = c, X2 = c, Y1 = 4, Y2 = size - 4, Stroke = Brushes.DimGray });
        int n = shape.Count;
        Point At(int i)
        {
            double a = 2 * Math.PI * i / n, m = Math.Min(shape[i], 1.08) * rad;
            return new Point(c + m * Math.Cos(a), c - m * Math.Sin(a));
        }
        var g = new PathGeometry();
        bool whole = shape.All(Has);
        if (whole)
        {
            var f = new PathFigure { StartPoint = At(0), IsClosed = true, IsFilled = true };
            f.Segments.Add(new PolyLineSegment(Enumerable.Range(1, n - 1).Select(At), true));
            g.Figures.Add(f);
        }
        else
        {
            // 測れていない区切りの次から、ぐるっと 1 周分を切れ目ごとの線にする
            int start = Enumerable.Range(0, n).First(i => !Has(shape[i]));
            PathFigure? f = null;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (!Has(shape[i])) { f = null; continue; }
                if (f is null) { f = new PathFigure { StartPoint = At(i), IsFilled = false }; f.Segments.Add(new PolyLineSegment()); g.Figures.Add(f); }
                else ((PolyLineSegment)f.Segments[0]).Points.Add(At(i));
            }
        }
        var orange = Brushes.Orange;
        cv.Children.Add(new System.Windows.Shapes.Path { Data = g, Stroke = orange, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Fill = whole ? new SolidColorBrush(Color.FromArgb(46, 255, 165, 0)) : null });
        return cv;
    }

    /// <summary>値の列を左から右へ。縦は 0〜1(1 を超える値があればそこまで)。数でない値で線を切る。</summary>
    static Canvas Curve(IReadOnlyList<double> shape, double w, double h)
    {
        const double pad = 10;
        double top = Math.Max(1, shape.Where(double.IsFinite).DefaultIfEmpty(1).Max());
        var cv = Board(w, h);
        foreach (double y in new[] { 0, 0.5, 1 })
        {
            double yy = h - pad - y / top * (h - 2 * pad);
            cv.Children.Add(new Line { X1 = pad, X2 = w - pad, Y1 = yy, Y2 = yy, Stroke = y == 0.5 ? Brushes.DimGray : Brushes.Gray, StrokeThickness = 1, StrokeDashArray = y == 0.5 ? [3, 3] : null });
        }
        var g = new PathGeometry();
        PathFigure? f = null;
        for (int i = 0; i < shape.Count; i++)
        {
            if (!double.IsFinite(shape[i])) { f = null; continue; }
            var p = new Point(pad + (w - 2 * pad) * i / (shape.Count - 1), h - pad - Math.Max(0, shape[i]) / top * (h - 2 * pad));
            if (f is null) { f = new PathFigure { StartPoint = p, IsFilled = false }; f.Segments.Add(new PolyLineSegment()); g.Figures.Add(f); }
            else ((PolyLineSegment)f.Segments[0]).Points.Add(p);
        }
        cv.Children.Add(new System.Windows.Shapes.Path { Data = g, Stroke = Brushes.Orange, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        return cv;
    }

    static Canvas Board(double w, double h) => new() { Width = w, Height = h, ClipToBounds = true, SnapsToDevicePixels = true };

    static UIElement Place(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    static TextBlock Text(string text, double size = 14, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    static TextBlock Weak(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
        return t;
    }
}
