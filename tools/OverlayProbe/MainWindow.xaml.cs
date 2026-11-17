// OverlayProbe の操作用の窓。開いたら重ねる窓を 1 枚出し、チェックで WS_EX_TRANSPARENT を付け外しする。
// この窓を閉じるとアプリが終わる(App.xaml の ShutdownMode)。
using System.Windows;

namespace OverlayProbe;

public partial class MainWindow : Window
{
    readonly OverlayWindow overlay;

    public MainWindow()
    {
        InitializeComponent();
        overlay = new OverlayWindow(); // 基底の Window ができたあとで作る(先に作るとこちらが MainWindow にならない)
        Loaded += (_, _) => overlay.Show();
    }

    void OnClickThrough(object sender, RoutedEventArgs e) =>
        Win32Window.SetClickThrough(overlay, ClickThrough.IsChecked == true);

    void OnExit(object sender, RoutedEventArgs e) => Close();
}
