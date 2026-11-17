// DummyGame の窓。どこを左クリックしても回数を 1 つ増やし、IsActive からフォーカスの有無を出す。
// Esc で閉じる。
using System.Windows;
using System.Windows.Input;

namespace DummyGame;

public partial class MainWindow : Window
{
    int clicks;

    public MainWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => CountText.Text = (++clicks).ToString();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Activated += (_, _) => ShowFocus();
        Deactivated += (_, _) => ShowFocus();
        ShowFocus();
    }

    void ShowFocus() => FocusText.Text = $"フォーカス: {(IsActive ? "あり" : "なし")}";
}
