// 最前面・クリック透過・フォーカスを取らない透明な窓の基底(仕様書 §9.10・D-5・D-21)。
// 中身(UIElement)を載せ、大きさの倍率、不透明度、背景の濃さ、「配置を直す」の状態(枠・名前・完了・つまみ)を持つ。
// 大きさの上限と端に寄せる距離は引数で受け、アプリの設定や文言の名前は持たない(D-25・INV-16)。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OverlayCore;

public class OverlayWindow : Window
{
    readonly Border _panel;
    readonly ScaleTransform _scale = new(1, 1);
    readonly Grid _chrome;
    readonly Grid _root;
    readonly DispatcherTimer _topmost;
    Size _baseSize;
    Point? _dragStart;
    Int32Rect _dragWindowStart;
    double _gripStartScale, _gripDx;
    bool _arranging;

    /// <param name="title">配置を直す間に左上に出す名前</param>
    /// <param name="content">パネルの中身</param>
    /// <param name="baseSize">倍率 100% のときの大きさ(DIP)</param>
    /// <param name="doneText">「完了」のボタンの文字</param>
    /// <param name="accent">配置を直す間の枠の色</param>
    public OverlayWindow(string title, FrameworkElement content, Size baseSize, string doneText, Brush accent)
    {
        _baseSize = baseSize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        Title = title;

        content.Width = baseSize.Width;
        content.Height = baseSize.Height;
        _panel = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), // 暗い画面の上でもパネルの範囲が分かるように
            BorderThickness = new Thickness(1),
            Child = content,
            LayoutTransform = _scale,
        };

        var border = new Rectangle { Stroke = accent, StrokeThickness = 2, StrokeDashArray = [4, 3], RadiusX = 8, RadiusY = 8, IsHitTestVisible = false };
        var name = new TextBlock
        {
            Text = title, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        };
        var done = new Button
        {
            Content = doneText, Margin = new Thickness(6), Padding = new Thickness(8, 2, 8, 2), Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        };
        done.Click += (_, _) => ArrangeDoneRequested?.Invoke(this, EventArgs.Empty);
        var grip = new Thumb
        {
            Width = 18, Height = 18, Margin = new Thickness(4), Cursor = Cursors.SizeNWSE, Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = accent,
        };
        grip.DragStarted += (_, _) => { _gripStartScale = _scale.ScaleX; _gripDx = 0; };
        grip.DragDelta += OnGripDelta;
        _chrome = new Grid { Visibility = Visibility.Collapsed };
        _chrome.Children.Add(border);
        _chrome.Children.Add(name);
        _chrome.Children.Add(done);
        _chrome.Children.Add(grip);

        _root = new Grid { Opacity = 0 };
        _root.Children.Add(_panel);
        _root.Children.Add(_chrome);
        Content = _root;

        _topmost = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Win32Window.ReassertTopmost(this), Dispatcher);
        SourceInitialized += (_, _) => Win32Window.AddOverlayStyles(this);
        MouseLeftButtonDown += OnDragStart;
        MouseMove += OnDragMove;
        MouseLeftButtonUp += OnDragEnd;
    }

    /// <summary>配置を直す間の「完了」が押された。</summary>
    public event EventHandler? ArrangeDoneRequested;

    /// <summary>つまみで倍率(%)を変えたい。受け手が上限で抑えてから PanelScale に入れる。</summary>
    public event Action<OverlayWindow, int>? ScaleRequested;

    /// <summary>ドラッグで動かし終えた。</summary>
    public event Action<OverlayWindow>? Moved;

    /// <summary>倍率 100% のときの大きさ(DIP)。中身の大きさも合わせて変える。</summary>
    public Size BaseSize
    {
        get => _baseSize;
        set
        {
            _baseSize = value;
            if (_panel.Child is FrameworkElement c)
            {
                c.Width = value.Width;
                c.Height = value.Height;
            }
        }
    }

    /// <summary>端に寄せる距離(DIP)。</summary>
    public double SnapDistance { get; set; } = 8;

    /// <summary>倍率(1.0 = 100%)。</summary>
    public double PanelScale
    {
        get => _scale.ScaleX;
        set { _scale.ScaleX = value; _scale.ScaleY = value; }
    }

    double _backgroundOpacity = 0.85;
    Color _backgroundColor = Colors.Black;

    /// <summary>背景の濃さ(0〜1)。</summary>
    public double BackgroundOpacity
    {
        set { _backgroundOpacity = Math.Clamp(value, 0, 1); UpdateBackground(); }
    }

    /// <summary>背景の色(不透明度は BackgroundOpacity で決める)。</summary>
    public Color BackgroundColor
    {
        set { _backgroundColor = value; UpdateBackground(); }
    }

    /// <summary>パネルの縁の色。</summary>
    public Color BorderColor
    {
        set => _panel.BorderBrush = new SolidColorBrush(value);
    }

    void UpdateBackground()
    {
        var c = _backgroundColor;
        c.A = (byte)Math.Round(255 * _backgroundOpacity);
        _panel.Background = new SolidColorBrush(c);
    }

    public bool Arranging
    {
        get => _arranging;
        set
        {
            _arranging = value;
            _chrome.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (new System.Windows.Interop.WindowInteropHelper(this).Handle != 0)
                Win32Window.SetClickThrough(this, !value);
        }
    }

    /// <summary>上限(作業領域の幅と高さに対する割合)に収まる一番大きい倍率(10% 刻み)へ抑える。</summary>
    public static int ClampScale(int percent, Size baseSize, MonitorArea monitor, double maxFraction)
    {
        double maxW = monitor.WorkWidthDip * maxFraction / baseSize.Width;
        double maxH = monitor.WorkHeightDip * maxFraction / baseSize.Height;
        int limit = (int)Math.Floor(Math.Min(maxW, maxH) * 10) * 10;
        return Math.Max(10, Math.Min(percent, limit));
    }

    /// <summary>フェードしながら出す。アニメーション効果が切なら、すぐ出す。</summary>
    public void ShowFaded()
    {
        if (!IsVisible)
            Show();
        _topmost.Start();
        Fade(1, null);
    }

    public void HideFaded()
    {
        _topmost.Stop();
        Fade(0, () => { if (_root.Opacity == 0) Hide(); });
    }

    void Fade(double to, Action? done)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _root.BeginAnimation(OpacityProperty, null);
            _root.Opacity = to;
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(100));
        if (done is not null)
            a.Completed += (_, _) => done();
        _root.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>モニターの作業領域の左上から (x, y) DIP の位置に置く。</summary>
    public void PlaceAt(MonitorArea m, double xDip, double yDip) =>
        Win32Window.MoveTo(this, m.Work.X + (int)Math.Round(xDip * m.Scale), m.Work.Y + (int)Math.Round(yDip * m.Scale));

    /// <summary>今いるモニターと、その作業領域の左上からの位置(DIP)。</summary>
    public (MonitorArea Monitor, double X, double Y) Placement()
    {
        var m = MonitorInfo.ForWindow(this);
        var r = Win32Window.GetRect(this);
        return (m, (r.X - m.Work.X) / m.Scale, (r.Y - m.Work.Y) / m.Scale);
    }

    public bool IsMostlyOffscreen() => MonitorInfo.IsMostlyOffscreen(Win32Window.GetRect(this), MonitorInfo.All());

    void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!_arranging || e.OriginalSource is Thumb || IsInsideButton(e.OriginalSource as DependencyObject))
            return;
        _dragStart = PointToScreen(e.GetPosition(this));
        _dragWindowStart = Win32Window.GetRect(this);
        CaptureMouse();
        e.Handled = true;
    }

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || !IsMouseCaptured)
            return;
        var now = PointToScreen(e.GetPosition(this));
        int x = _dragWindowStart.X + (int)(now.X - start.X);
        int y = _dragWindowStart.Y + (int)(now.Y - start.Y);
        var m = MonitorInfo.ForWindow(this);
        int snap = (int)Math.Round(SnapDistance * m.Scale);
        var w = m.Work;
        int width = _dragWindowStart.Width, height = _dragWindowStart.Height;
        if (Math.Abs(x - w.X) <= snap) x = w.X;
        if (Math.Abs(x + width - (w.X + w.Width)) <= snap) x = w.X + w.Width - width;
        if (Math.Abs(y - w.Y) <= snap) y = w.Y;
        if (Math.Abs(y + height - (w.Y + w.Height)) <= snap) y = w.Y + w.Height - height;
        Win32Window.MoveTo(this, x, y);
    }

    void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
            return;
        _dragStart = null;
        ReleaseMouseCapture();
        Moved?.Invoke(this);
    }

    void OnGripDelta(object sender, DragDeltaEventArgs e)
    {
        _gripDx += e.HorizontalChange;
        double width = _baseSize.Width * _gripStartScale;
        double target = _gripStartScale * (width + _gripDx) / width;
        int percent = (int)Math.Round(target * 10) * 10;
        if (percent != (int)Math.Round(PanelScale * 100))
            ScaleRequested?.Invoke(this, percent);
    }

    static bool IsInsideButton(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase)
                return true;
        return false;
    }
}
