// S7 計測(画面設計書 §4.8b・仕様書 FR-53・§9.19)。連打テスト(3・2・1 の後 10 秒)と反応速度テスト(5 回)を行う。
// 押下はスナップショットの PressCounter の増え方と LastPressNs から拾い、時刻はすべて SDL の ns で比べる。計算は Core の RapidStats・ReactionStats。
// 結果は保存しない。Esc・タブを離れる・パッドが無くなる・デモを流すで止め、止めたら結果を出さない。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PadSight.Core;

namespace PadSight.App.Main;

public sealed class MeasureTab : UserControl
{
    const ulong S = Values.NsPerSecond;
    const ulong RapidNs = 10 * S;
    /// <summary>終わりの時刻を過ぎてから、まだ届いていない押下を待つ時間(SDL のスレッドからスナップショットが出るまでの遅れの分)。</summary>
    const ulong GraceNs = 80 * Values.NsPerMs;
    const int Rounds = 5;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    enum Mode { Idle, RapidCountdown, Rapid, ReactWait, ReactGo }

    readonly Button _rapidBtn, _reactBtn;
    readonly TextBlock _rapidStatus = Ui.Weak(), _reactStatus = Ui.Weak();
    readonly TextBlock _big = BigText(84);
    readonly TextBlock _bigSub = Ui.Weak();
    readonly Border _barFill = new() { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(3), RenderTransform = new ScaleTransform(0, 1) };
    readonly TextBlock _rapidResult = Ui.Text(), _rapidIntervals = Ui.Text();
    readonly Border _square = new() { Height = 170, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBlock _squareText = BigText(30);
    readonly TextBlock _rounds = Ui.Text(), _reactResult = Ui.Text();

    Mode _mode;
    long _counter;
    ulong _phaseEnd, _rapidStart, _prevPress, _shownNs;
    readonly List<ulong> _presses = [];
    readonly List<double> _times = [];

    public MeasureTab()
    {
        _rapidBtn = Ui.Button("始める", StartRapid);
        _reactBtn = Ui.Button("始める", StartReaction);
        _barFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _square.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        _square.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _square.Child = _squareText;
        _squareText.HorizontalAlignment = HorizontalAlignment.Center;
        _squareText.FontFamily = new FontFamily("Yu Gothic UI");
        _bigSub.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var t in new[] { _rapidResult, _rapidIntervals, _rounds, _reactResult })
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        _rapidResult.FontSize = _reactResult.FontSize = 18;
        _rapidResult.FontWeight = _reactResult.FontWeight = FontWeights.SemiBold;

        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Child = _barFill, Margin = new Thickness(0, 4, 0, 12) };
        track.SetResourceReference(Border.BackgroundProperty, "BorderBrush");

        var rapid = new StackPanel();
        rapid.Children.Add(Ui.Text("どれかのボタンを 10 秒、できるだけ速く押してください"));
        rapid.Children.Add(Ui.Horizontal(_rapidBtn, _rapidStatus));
        rapid.Children.Add(_big);
        rapid.Children.Add(_bigSub);
        rapid.Children.Add(track);
        rapid.Children.Add(_rapidResult);
        rapid.Children.Add(_rapidIntervals);

        var react = new StackPanel();
        react.Children.Add(Ui.Text("四角の色が変わったら、どれかのボタンをすぐ押してください。5 回測ります"));
        react.Children.Add(Ui.Horizontal(_reactBtn, _reactStatus)); // 「始める」は連打テストと同じく説明のすぐ下
        react.Children.Add(_square);
        react.Children.Add(_rounds);
        react.Children.Add(_reactResult);
        react.Children.Add(Ui.Weak("画面に出るまでの遅れ(数十ミリ秒)を含みます"));

        // ほかのタブと同じく左にそろえた箱に入れる(見出しは箱の中)
        Content = Ui.Page(Ui.Card("連打テスト", rapid), Ui.Card("反応速度テスト", react));
        ResetDisplays();
    }

    static TextBlock BigText(double size)
    {
        var t = Ui.Text();
        t.FontFamily = new FontFamily("Consolas");
        t.FontSize = size;
        t.FontWeight = FontWeights.Bold;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public bool Running => _mode != Mode.Idle;

    ulong Now => SnapshotBus.NowNs(App.Current.Bus.Latest);

    void StartRapid()
    {
        Cancel();
        _counter = App.Current.Bus.Latest.PressCounter;
        _presses.Clear();
        _rapidResult.Text = _rapidIntervals.Text = "";
        _mode = Mode.RapidCountdown;
        _phaseEnd = Now + 3 * S;
    }

    void StartReaction()
    {
        Cancel();
        _times.Clear();
        _rounds.Text = _reactResult.Text = "";
        NextRound(Now, early: false);
    }

    /// <summary>1.5〜4 秒のでたらめな待ちに入る。</summary>
    void NextRound(ulong now, bool early)
    {
        _counter = App.Current.Bus.Latest.PressCounter;
        _mode = Mode.ReactWait;
        _phaseEnd = now + (ulong)((1.5 + Random.Shared.NextDouble() * 2.5) * S);
        SetSquare(go: false, early ? "早すぎます。もう一度" : "まだです");
        _reactStatus.Text = $"{_times.Count + 1} 回目 / {Rounds} 回";
    }

    /// <summary>止める。止めたら結果を出さない。</summary>
    public void Cancel()
    {
        if (_mode is Mode.RapidCountdown or Mode.Rapid) { _presses.Clear(); _rapidResult.Text = _rapidIntervals.Text = ""; }
        if (_mode is Mode.ReactWait or Mode.ReactGo) { _times.Clear(); _rounds.Text = _reactResult.Text = ""; }
        _mode = Mode.Idle;
        ResetDisplays();
    }

    void ResetDisplays()
    {
        if (_presses.Count == 0 && _rapidResult.Text == "")
        {
            _big.Text = "—";
            _bigSub.Text = "「始める」を押すと、3 秒数えてから始まります"; // 待ちの状態を文字でも出す(画面設計書 §6。2026-11-18)
        }
        ((ScaleTransform)_barFill.RenderTransform).ScaleX = 0;
        SetSquare(go: false, "「始める」を押すと始まります");
        _squareText.FontSize = 16; // 待ちの案内は小さく。測っている間の文字(SetSquare の呼び先)は 30 に戻す
        _squareText.FontWeight = FontWeights.Normal;
        _reactStatus.Text = "";
    }

    void SetSquare(bool go, string text)
    {
        _squareText.Text = text;
        _squareText.FontSize = 30;
        _squareText.FontWeight = FontWeights.Bold;
        if (go)
        {
            // 押したときの色にして、線も太くする(色だけで示さない。画面設計書 §6)
            var c = (Color)ColorConverter.ConvertFromString(App.Current.Settings.PressedColor);
            _square.Background = new SolidColorBrush(c);
            _square.BorderThickness = new Thickness(6);
            _square.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            _squareText.Foreground = new SolidColorBrush(0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140 ? Color.FromRgb(0x1E, 0x1F, 0x22) : Colors.White);
        }
        else
        {
            _square.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            _square.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            _square.BorderThickness = new Thickness(2);
            _squareText.SetResourceReference(TextBlock.ForegroundProperty, text.StartsWith("早すぎ", StringComparison.Ordinal) ? "WarningTextBrush" : "SubTextBrush"); // 明るいテーマでも地との差が 4.5:1 になる警告の文字色
        }
    }

    public void Render(PadSnapshot s, ulong now)
    {
        bool demo = App.Current.DemoRunning; // デモの押下は別の数え方なので、測らない
        bool can = s.Pads.Count > 0 && !demo;
        if (!can && Running)
            Cancel();
        _rapidBtn.IsEnabled = _reactBtn.IsEnabled = can && !Running;
        string why = can ? "" : demo ? "デモを流している間は測れません" : "パッドが見つかりません";
        if (!Running)
            _rapidStatus.Text = _reactStatus.Text = why;

        long k = s.PressCounter - _counter;
        if (k < 0) k = 0; // 数え方が入れ替わった(つなぎ直しなど)。今から数える
        _counter = s.PressCounter;

        switch (_mode)
        {
            case Mode.RapidCountdown:
                if (now < _phaseEnd)
                {
                    _big.Text = Math.Ceiling((_phaseEnd - now) / (double)S).ToString("0", Inv);
                    _bigSub.Text = "準備してください";
                    break;
                }
                // 数え下げの終わりの時刻を始まりにする(描画の時刻ではなく、押下と同じ SDL の時刻で区切る)
                _mode = Mode.Rapid;
                _rapidStart = _prevPress = _phaseEnd;
                _phaseEnd = _rapidStart + RapidNs;
                goto case Mode.Rapid;
            case Mode.Rapid:
                if (k > 0)
                {
                    // スナップショットの間に何回押されても、回数は PressCounter の増えた分。時刻は最後の物しか分からないので、間を等分して置く
                    foreach (var t in RapidStats.Spread(_prevPress, s.LastPressNs, k))
                        if (t >= _rapidStart && t < _phaseEnd)
                            _presses.Add(t);
                    _prevPress = Math.Max(_prevPress, s.LastPressNs);
                }
                ulong elapsed = now > _rapidStart ? Math.Min(RapidNs, now - _rapidStart) : 0;
                ((ScaleTransform)_barFill.RenderTransform).ScaleX = elapsed / (double)RapidNs;
                _big.Text = _presses.Count.ToString("N0", Inv);
                _bigSub.Text = $"残り {Math.Ceiling((RapidNs - elapsed) / (double)S).ToString("0", Inv)} 秒";
                if (now >= _phaseEnd + GraceNs)
                    FinishRapid();
                break;
            case Mode.ReactWait:
                if (k > 0)
                    NextRound(now, early: true);
                else if (now >= _phaseEnd)
                {
                    SetSquare(go: true, "押して!");
                    _mode = Mode.ReactGo;
                    _shownNs = Now; // 色を変えた時刻(SDL の時刻に直した物)
                }
                break;
            case Mode.ReactGo when k > 0:
                if (s.LastPressNs < _shownNs)
                {
                    NextRound(now, early: true); // 色が変わる前の押下が、後のスナップショットで届いた
                    break;
                }
                // 2 回以上まとめて届いたときは最後の押下の時刻になる(最初の押下より 1 フレームほど遅い側にずれる)
                _times.Add((s.LastPressNs - _shownNs) / (double)Values.NsPerMs);
                _rounds.Text = string.Join("   ", _times.Select((ms, i) => $"{i + 1} 回目 {ms.ToString("0", Inv)} ms"));
                if (_times.Count < Rounds)
                    NextRound(now, early: false);
                else
                    FinishReaction();
                break;
        }
    }

    void FinishRapid()
    {
        var r = RapidStats.From(_presses, RapidNs / (double)S);
        _mode = Mode.Idle;
        _big.Text = r.Count.ToString("N0", Inv);
        _bigSub.Text = "回";
        ((ScaleTransform)_barFill.RenderTransform).ScaleX = 1;
        _rapidResult.Text = $"{r.Count.ToString("N0", Inv)} 回(1 秒あたり {r.PerSecond.ToString("0.0", Inv)}、1 秒の最大 {r.Max1s.ToString(Inv)})";
        _rapidIntervals.Text = r.MeanIntervalMs is double a && r.StdevIntervalMs is double sd
            ? $"押した間隔 平均 {a.ToString("0.0", Inv)} ms、ばらつき {sd.ToString("0.0", Inv)} ms"
            : "押した間隔 2 回以上押すと出ます";
        _rapidStatus.Text = "";
    }

    void FinishReaction()
    {
        _mode = Mode.Idle;
        SetSquare(go: false, "おしまい");
        _reactStatus.Text = "";
        if (ReactionStats.From(_times) is { } r)
            _reactResult.Text = $"平均 {r.AverageMs.ToString("0", Inv)} ms、最良 {r.BestMs.ToString("0", Inv)} ms";
    }
}
