// S3 診断の「スティック」(画面設計書 §4.7・仕様書 §9.9・FR-23・FR-56・FR-60)。前からの「手を離して測る」「外周を測る」と、PadLab から移したスナップバック・分解能・応答カーブ・デッドゾーンのおすすめ値。
// ずれと外周は描画のたびのスナップショットの値で、精度の 3 つは測っている間だけ受け取る DiagSample で測る。時刻はどれも SDL の ns(DiagContext.NowNs)。計算は Core。
// Esc・ページを離れる・パッドやスティックを替えると止め、止めたら結果を出さない。結果は「この結果を残す」で DiagStore に 1 件ずつ残す(時刻つきの入力は入れない。INV-8)。
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PadSight.App.Panels;
using PadSight.Core;
using PadSight.Core.Diag;

namespace PadSight.App.Main;

public sealed class StickPage : IDiagPage
{
    const double Fig = 240, C = 120, R = 100;
    const ulong S = Values.NsPerSecond;
    const ulong WaitNs = 3 * S, DriftNs = 5 * S, OuterNs = 10 * S, ResolutionNs = 5 * S, LimitNs = 30 * S;
    const int Releases = 5;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Brush OverBrush = Draw.Freeze(new SolidColorBrush(Color.FromRgb(0xED, 0x42, 0x45)));

    enum Test { None, Drift, Outer, Snapback, Resolution, Linearity }

    readonly RadioButton _left = new() { Content = "左", IsChecked = true, GroupName = "StickPageSide", Margin = new Thickness(0, 0, 16, 0) };
    readonly RadioButton _right = new() { Content = "右", GroupName = "StickPageSide" };
    readonly List<Button> _buttons = [];
    readonly Button _stop;

    // 測っている間の案内(手順・残り・進み具合)
    readonly Border _guide = new() { BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 8, 0, 4) };
    readonly TextBlock _guideTitle = Ui.Text();
    readonly TextBlock _guideSteps = Ui.Weak();
    readonly Border _bar = new() { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 8, 0, 0) };
    readonly ScaleTransform _barScale = new(0, 1);

    // 図(パネルの色の台)
    readonly Canvas _fig = new() { Width = Fig, Height = Fig, ClipToBounds = true, Background = Brushes.Transparent };
    readonly Ellipse _unit = Ring(R, Draw.Faint);
    readonly Ellipse _dz = Ring(10, Draw.Weak, dashed: true);
    readonly Line _axX = new() { X1 = 12, X2 = Fig - 12, Y1 = C, Y2 = C, Stroke = Draw.Faint };
    readonly Line _axY = new() { X1 = C, X2 = C, Y1 = 12, Y2 = Fig - 12, Stroke = Draw.Faint };
    readonly Polyline _outline = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    readonly Canvas _plot = new();
    readonly Ellipse _dot = new() { Width = 9, Height = 9, Fill = Draw.TextBrush };

    // 結果
    readonly TextBlock _resultTitle = Ui.SubSection("結果");
    readonly TextBlock _resultNote = Ui.Weak();
    readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly Border _verdict = new() { BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _verdictText = Ui.Text();
    readonly Button _save;
    readonly TextBlock _saved = Ui.Weak();
    readonly StackPanel _dzBody = new();

    Test _test, _shown; // 測っている(最後に測った)物と、図に出している物
    bool _running, _waiting;
    ulong _end, _lastNow;
    ulong? _resStartNow;
    uint? _padId;
    string _padName = "", _padKind = "";
    readonly List<StickValue> _values = [];
    SnapbackTest? _snap;
    ResolutionTest? _res;
    LinearityTest? _lin;
    ulong? _lastSampleNs;
    ulong _nowAtLastSample;
    DriftResult? _shownDrift;
    float[]? _shownBins;
    readonly DriftResult?[] _drift = new DriftResult?[2]; // 左・右の最後のずれ(おすすめ値に使う)
    Func<DiagRecord>? _record;

    public string Title => "スティック";
    public FrameworkElement View { get; }
    public bool WantsSamples => _running && _test is Test.Snapback or Test.Resolution or Test.Linearity;

    bool Right => _right.IsChecked == true;
    string Side => Right ? "右" : "左";

    public StickPage()
    {
        _left.Checked += (_, _) => SideChanged();
        _right.Checked += (_, _) => SideChanged();
        Button Add(string text, Test t)
        {
            var b = Ui.Button(text, () => Start(t));
            _buttons.Add(b);
            return b;
        }
        var center = Ui.Horizontal(Add("手を離して測る", Test.Drift), Add("外周を測る", Test.Outer));
        var precision = Ui.Horizontal(Add("スナップバック", Test.Snapback), Add("分解能", Test.Resolution), Add("応答カーブ", Test.Linearity));
        _stop = Ui.Button("やめる", Cancel);
        _save = Ui.Button("この結果を残す", Save);

        // 案内の箱: 帯と同じく、面の色に強調色の線を左に付ける
        _guide.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        _guide.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _guideTitle.FontWeight = FontWeights.SemiBold;
        _guideTitle.FontSize = 15;
        Typography.SetNumeralAlignment(_guideTitle, FontNumeralAlignment.Tabular);
        _guideSteps.TextWrapping = TextWrapping.Wrap;
        _guideSteps.LineHeight = 21;
        var fill = new Border { CornerRadius = new CornerRadius(3), RenderTransform = _barScale, HorizontalAlignment = HorizontalAlignment.Stretch };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _bar.Child = fill;
        _bar.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        var guide = new DockPanel();
        _stop.VerticalAlignment = VerticalAlignment.Top;
        _stop.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_stop, Dock.Right);
        guide.Children.Add(_stop);
        var guideText = new StackPanel();
        guideText.Children.Add(_guideTitle);
        guideText.Children.Add(_guideSteps);
        guideText.Children.Add(_bar);
        guide.Children.Add(guideText);
        _guide.Child = guide;
        _guide.Visibility = Visibility.Collapsed;

        // 図
        foreach (var e in new UIElement[] { _unit, _dz, _axX, _axY, _outline, _plot, _dot })
            _fig.Children.Add(e);
        var picture = Ui.Picture(_fig);
        picture.Margin = new Thickness(0, 4, 24, 8);

        // 結果
        _verdict.Child = _verdictText;
        _verdict.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        _resultTitle.Margin = new Thickness(0, 0, 0, 4);
        _resultNote.TextWrapping = TextWrapping.Wrap;
        _saved.Margin = new Thickness(4, 0, 0, 0);
        var res = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        res.Children.Add(_resultTitle);
        res.Children.Add(_resultNote);
        res.Children.Add(_tiles);
        res.Children.Add(_verdict);
        res.Children.Add(Ui.Horizontal(_save, _saved));
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(picture);
        Grid.SetColumn(res, 1);
        body.Children.Add(res);

        var foot = Ui.Weak("値は SDL を通した値で、ゲームが受け取る値と同じとは限りません。測った値は目安で、故障を言い切るものではありません");
        foot.TextWrapping = TextWrapping.Wrap;
        const double lw = 120;
        var stick = Ui.Card(null, // 左の一覧が「スティック」なので、見出しは重ねない
            Ui.Row("スティック", Ui.Horizontal(_left, _right), lw),
            Ui.Row("中心と外周", center, lw),
            Ui.Row("精度", precision, lw),
            _guide,
            body,
            foot);
        var page = Ui.Page(stick, Ui.Card("デッドゾーンのおすすめ値", _dzBody));
        page.Padding = new Thickness(0, 0, 12, 4); // 外枠が余白を持つので、上と左は空けない
        View = page;
        ClearResult();
        UpdateDeadzone();
        DrawBase();
    }

    // ---- 始める・止める

    void Start(Test t)
    {
        Cancel();
        ClearResult();
        _resultNote.Text = "測り終えると、ここに結果が出ます";
        _test = _shown = t;
        _running = true;
        _waiting = t == Test.Drift;
        _end = _lastNow + t switch { Test.Drift => WaitNs, Test.Outer => OuterNs, _ => LimitNs };
        _values.Clear();
        _shownDrift = null;
        _shownBins = null;
        _lastSampleNs = null;
        _resStartNow = null;
        _snap = t == Test.Snapback ? new SnapbackTest(Right, Releases) : null;
        _res = t == Test.Resolution ? new ResolutionTest(Right, ResolutionNs / (double)S) : null;
        _lin = t == Test.Linearity ? new LinearityTest(Right) : null;
        _snap?.Start();
        _res?.Start();
        _lin?.Start();
        _plot.Children.Clear();
        _outline.Points = [];
        DrawBase();
    }

    /// <summary>止めたら結果を出さない。測っていなければ何もしない(ページを替えるたびに呼ばれる。出ている結果は残す)。</summary>
    public void Cancel()
    {
        if (!_running)
            return;
        _running = _waiting = false;
        _snap = null;
        _res = null;
        _lin = null;
        _values.Clear();
        _guide.Visibility = Visibility.Collapsed;
        _plot.Children.Clear();
        _outline.Points = [];
        _shown = Test.None;
        _resultNote.Text = "測る物を選ぶと、ここに結果が出ます。測った結果は「この結果を残す」で履歴に残せます";
        DrawBase();
    }

    void SideChanged()
    {
        Cancel();
        ClearShown(); // 出ている結果は前のスティックの物なので消す
        UpdateDeadzone();
    }

    /// <summary>出している結果と図を消す(パッドやスティックを替えたとき)。</summary>
    void ClearShown()
    {
        ClearResult();
        _shown = Test.None;
        _shownDrift = null;
        _shownBins = null;
        _values.Clear();
        _plot.Children.Clear();
        _outline.Points = [];
        DrawBase();
    }

    // ---- 描画のたび

    public void Render(DiagContext c)
    {
        _lastNow = c.NowNs;
        var pad = c.Pad;
        StickValue? v = pad is not null && c.S.Sticks.TryGetValue(pad.InstanceId, out var st) ? (Right ? st.Right : st.Left) : null;
        if (_running && pad?.InstanceId != _padId)
            Cancel();
        foreach (var b in _buttons)
            b.IsEnabled = v is not null;
        if (v is not StickValue cur || pad is null)
        {
            ShowGuide("パッドが見つかりません", "パッドをつなぐと測れます", -1, stop: false);
            _dot.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_running)
        {
            if (_padId is not null && _padId != pad.InstanceId)
            {
                // 別のパッドに替えた: 前のパッドの結果とおすすめ値は消す
                Array.Clear(_drift);
                ClearShown();
                UpdateDeadzone();
            }
            _padId = pad.InstanceId;
            _padName = pad.Name;
            _padKind = pad.Kind.ToString();
            _guide.Visibility = Visibility.Collapsed;
            DrawBase(); // デッドゾーンの設定が変わっても円が付いてくるように
        }
        ulong now = c.NowNs;
        ulong rest = Values.Since(_end, now);
        string left = Math.Ceiling(rest / (double)S).ToString(Inv);
        switch (_running ? _test : Test.None)
        {
            case Test.Drift when _waiting:
                ShowGuide($"スティックから手を離してください。{left} 秒後に測り始めます",
                    "パッドを机に置くと、手の揺れが入らずに測れます", 1 - rest / (double)WaitNs);
                if (rest == 0)
                {
                    _waiting = false;
                    _end = now + DriftNs;
                    _values.Clear();
                }
                break;
            case Test.Drift:
                _values.Add(cur);
                ShowGuide($"測っています。そのまま触らないでください。残り {left} 秒",
                    "触っていないときの位置を集めています。図は中心の近くを拡大しています", 1 - rest / (double)DriftNs);
                _shownDrift = StickDiagnostics.Drift(_values);
                if (rest == 0)
                    FinishDrift();
                break;
            case Test.Outer:
                _values.Add(cur);
                ShowGuide($"スティックを外周に沿って、ゆっくり 3 周回してください。残り {left} 秒",
                    "いちばん外まで倒したまま回します。速く回すと区切りが抜けることがあります", 1 - rest / (double)OuterNs);
                var o = StickDiagnostics.Outer(_values);
                DrawOutline(o.BinMax);
                if (rest == 0)
                    FinishOuter(o);
                break;
            case Test.Snapback when _snap is not null:
                Feed(c, _snap.Feed);
                if (c.Samples.Count == 0 && _lastSampleNs is ulong last)
                    _snap.Hold(last + Values.Since(now, _nowAtLastSample)); // 止まると何も届かないので、時刻だけ進める
                var sr = _snap.Result;
                ShowGuide($"スティックをいっぱいに倒して、指をはじくように離してください。あと {Releases - sr.Releases} 回",
                    "1. 端までしっかり倒す   2. 押し戻さずに指を外す   3. 中央で止まるのを待つ\n向きを変えながら 5 回くり返すと、離したときの振れと戻る時間を測ります", sr.Releases / (double)Releases);
                DrawSnapback(sr);
                if (sr.Complete)
                    FinishSnapback();
                else if (rest == 0)
                {
                    _snap.Finish();
                    FinishSnapback();
                }
                break;
            case Test.Resolution when _res is not null:
                Feed(c, _res.Feed);
                if (_res.Started)
                    _resStartNow ??= now;
                if (_resStartNow is not ulong t0)
                    ShowGuide("スティックを真横に、できるだけゆっくり小さく動かしてください", "動かし始めると 5 秒測ります。値の刻みの細かさを見ます", 0);
                else
                {
                    ulong r5 = Values.Since(t0 + ResolutionNs, now);
                    ShowGuide($"そのまま、ゆっくり小さく動かし続けてください。残り {Math.Ceiling(r5 / (double)S).ToString(Inv)} 秒",
                        "大きく倒す必要はありません。少しずつ、止めずに動かします", 1 - r5 / (double)ResolutionNs);
                    if (r5 == 0) _res.Finish(); // 動かすのをやめて値が届かなくなっても、5 秒で終える
                }
                DrawResolution(_res.Trace);
                if (_res.Result.Complete)
                    FinishResolution();
                else if (rest == 0)
                    Message("動きがありませんでした。もう一度測ってください");
                break;
            case Test.Linearity when _lin is not null:
                Feed(c, _lin.Feed);
                var lr = _lin.Result;
                ShowGuide("中央から真横へ、一定の速さでゆっくり倒してください。端まで倒すと終わります",
                    "2〜3 秒かけて端まで倒すのが目安です。速さのむらも線に出ます", lr.Curve.Count == 0 ? 0 : lr.Curve[^1].Y / LinearityTest.EndRadius);
                DrawLinearity(lr, done: false);
                if (lr.Complete)
                    FinishLinearity();
                else if (rest == 0)
                {
                    _lin.Finish();
                    if (_lin.Result.Complete) FinishLinearity();
                    else Message("スティックが動きませんでした。もう一度測ってください");
                }
                break;
        }
        DrawDot(cur);
    }

    void Feed(DiagContext c, Action<DiagSample> feed)
    {
        foreach (var s in c.Samples)
            feed(s);
        if (c.Samples.Count > 0)
        {
            _lastSampleNs = c.Samples[^1].Ns;
            _nowAtLastSample = c.NowNs;
        }
    }

    // ---- 終わり

    void FinishDrift()
    {
        var d = StickDiagnostics.Drift(_values);
        double dz = App.Current.Settings.Deadzone;
        _drift[Right ? 1 : 0] = d;
        var advice = DeadzoneAdvisor.Compute(d.MaxMagnitude, d.Noise, null);
        Done("手を離したときのずれ");
        _shownDrift = d;
        AddTile("平均のずれ", F3(d.MeanMagnitude), $"生の値 x={d.RawMeanX.ToString("+0;-0", Inv)}、y={d.RawMeanY.ToString("+0;-0", Inv)}");
        AddTile("最大", F3(d.MaxMagnitude));
        AddTile($"デッドゾーンの円({dz.ToString("0.00", Inv)})より", d.MaxMagnitude <= dz ? "内側" : "外側");
        AddTile("動きの最大", F3(d.MaxFromMean), "平均の位置から");
        AddTile("揺れ", F3(d.Noise), "平均の位置からの散らばり");
        SetVerdict(d.Verdict, d.Tier == 0 ? "SuccessBrush" : "WarningBrush");
        UpdateDeadzone();
        _record = Rec("stick_drift", "手を離したときのずれ", d.Verdict, [],
            ("平均のずれ", d.MeanMagnitude), ("最大のずれ", d.MaxMagnitude), ("生の値の平均 x", Math.Round(d.RawMeanX, 1)), ("生の値の平均 y", Math.Round(d.RawMeanY, 1)),
            ("動きの最大", d.MaxFromMean), ("揺れ", d.Noise), ("デッドゾーンの円", dz), ("デッドゾーンのおすすめ値(%)", advice.RecommendedPercent));
        DrawBase();
        DrawDrift();
    }

    void FinishOuter(OuterResult o)
    {
        if (o.Insufficient)
        {
            Message("回し方が足りません。もう一度測ってください");
            return;
        }
        Done("外周の形");
        _shownBins = o.BinMax;
        AddTile("平均", F3(o.Mean));
        AddTile("最小", F3(o.Min));
        AddTile("最大", F3(o.Max));
        AddTile("1.00 からのずれの平均", o.DeviationPercent.ToString("0.0", Inv) + "%");
        AddTile("真円度", o.Roundness.ToString("0.00", Inv), "最小 ÷ 最大");
        AddTile("左右の差", o.AsymmetryX.ToString("+0.00;-0.00;0.00", Inv), "正なら右が大きい");
        AddTile("上下の差", o.AsymmetryY.ToString("+0.00;-0.00;0.00", Inv), "正なら上が大きい");
        Ui.SetText(_resultNote, "形の良し悪しは決めません。図の線と円を見比べてください");
        _record = Rec("stick_outer", "外周の形", "", o.Shape,
            ("平均", o.Mean), ("最小", o.Min), ("最大", o.Max), ("1.00 からのずれの平均(%)", o.DeviationPercent),
            ("真円度", o.Roundness), ("左右の差", o.AsymmetryX), ("上下の差", o.AsymmetryY), ("値の入らなかった区切り", o.EmptyBins));
        DrawBase();
        DrawOutline(o.BinMax);
    }

    void FinishSnapback()
    {
        var r = _snap!.Result;
        if (!r.Complete)
        {
            Message("離したところを測れませんでした。端まで倒してから、指をはじくように離してください");
            return;
        }
        Done("スナップバック");
        AddTile("戻るまで(平均)", r.MeanReturnMs.ToString("0.0", Inv) + " ms");
        AddTile("戻るまで(最大)", r.MaxReturnMs.ToString("0.0", Inv) + " ms");
        AddTile("反対側への振れ(最大)", F3(r.MaxOvershoot));
        AddTile("測った回数", $"{r.Releases} / {r.TargetReleases}");
        SetVerdict(r.Verdict, r.MaxOvershoot >= SnapbackResult.OvershootNotable ? "WarningBrush" : "SuccessBrush");
        _record = Rec("snapback", "スナップバック", r.Verdict, [],
            ("戻るまで(平均・ms)", Math.Round(r.MeanReturnMs, 1)), ("戻るまで(最大・ms)", Math.Round(r.MaxReturnMs, 1)),
            ("反対側への振れ(最大)", r.MaxOvershoot), ("測った回数", r.Releases));
        DrawSnapback(r);
    }

    void FinishResolution()
    {
        var r = _res!.Result;
        Done("分解能");
        AddTile("最小の刻み", r.AxisStep.ToString("0.00000", Inv), "x・y の値の差から");
        AddTile("刻みの目安", r.StepsEstimate > 0 ? $"約 {r.StepsEstimate.ToString("N0", Inv)} 段" : "—", "−1〜1 を最小の刻みで割った数");
        AddTile("出た値の数", r.DistinctLevels.ToString(Inv));
        AddTile("揺れ", r.NoiseLevel.ToString("0.0000", Inv), "続いた値の差のばらつき");
        AddTile("測った時間", r.ElapsedSeconds.ToString("0.0", Inv) + " 秒");
        bool coarse = r.Coarse || r.DistinctLevels < 20;
        SetVerdict(r.Verdict, coarse ? "WarningBrush" : "SuccessBrush");
        _record = Rec("resolution", "分解能", r.Verdict, [],
            ("最小の刻み", r.AxisStep), ("|v| の最小の差", r.MinStep), ("刻みの目安(段)", r.StepsEstimate), ("出た値の数", r.DistinctLevels),
            ("揺れ", r.NoiseLevel), ("測った時間(秒)", Math.Round(r.ElapsedSeconds, 2)));
        DrawResolution(_res.Trace);
    }

    void FinishLinearity()
    {
        var r = _lin!.Result;
        Done("応答カーブ");
        AddTile("直線からのずれ(最大)", F3(r.MaxDeviation), "最初と最後を結んだ線から");
        AddTile("飛び", r.JumpCount.ToString(Inv) + " か所", "0.12 を超える急な増え");
        AddTile("戻り", r.ReversalCount.ToString(Inv) + " か所", "0.01 を超える減り");
        AddTile("最大の飛び", F3(r.MaxJump));
        bool warn = r.JumpCount > 0 || r.ReversalCount > 0 || r.Curve[^1].Y <= LinearityTest.EndRadius;
        SetVerdict(r.Verdict, warn ? "WarningBrush" : "SuccessBrush");
        _record = Rec("linearity", "応答カーブ", r.Verdict, [],
            ("直線からのずれ(最大)", r.MaxDeviation), ("飛び", r.JumpCount), ("戻り", r.ReversalCount), ("最大の飛び", r.MaxJump));
        DrawLinearity(r, done: true);
    }

    /// <summary>測り終えた(結果の欄を出す)。</summary>
    void Done(string title)
    {
        _running = _waiting = false;
        _guide.Visibility = Visibility.Collapsed;
        ClearResult();
        _resultTitle.Text = $"結果  ・  {title}({Side})";
        _save.Visibility = Visibility.Visible;
        _save.IsEnabled = true;
    }

    /// <summary>測れなかった(数を出さず、もう一度を求める文だけ)。</summary>
    void Message(string text)
    {
        _running = _waiting = false;
        _guide.Visibility = Visibility.Collapsed;
        ClearResult();
        _resultNote.Visibility = Visibility.Collapsed;
        SetVerdict(text, "WarningBrush");
        _plot.Children.Clear();
        _outline.Points = [];
        _shown = Test.None;
        DrawBase();
    }

    void ClearResult()
    {
        _tiles.Children.Clear();
        _resultTitle.Text = "結果";
        Ui.SetText(_resultNote, "測る物を選ぶと、ここに結果が出ます。測った結果は「この結果を残す」で履歴に残せます");
        SetVerdict("", "");
        _save.Visibility = Visibility.Collapsed;
        _saved.Text = "";
        _record = null;
    }

    void Save()
    {
        if (_record is null)
            return;
        bool ok = App.Current.Diagnostics.Save(_record()) is not null;
        _saved.Text = ok ? "残しました" : "残せませんでした(ログを見てください)";
        _save.IsEnabled = !ok;
    }

    /// <summary>残す記録を、測り終えたときのパッド・スティック・時刻で作っておく。</summary>
    Func<DiagRecord> Rec(string kind, string title, string verdict, IReadOnlyList<double> shape, params (string Name, double Value)[] values)
    {
        var rec = new DiagRecord(kind, $"{title}({Side})", DateTime.UtcNow, _padName, _padKind,
            [.. values.Select(v => new KeyValuePair<string, double>(v.Name, Math.Round(v.Value, 5)))], verdict, shape, App.Version);
        return () => rec;
    }

    // ---- 結果の部品

    static string F3(double v) => v.ToString("0.000", Inv);

    /// <summary>数の札(上に小さいラベル、大きい数、あれば下に弱い補足)。</summary>
    void AddTile(string label, string value, string sub = "")
    {
        _resultNote.Visibility = Visibility.Collapsed;
        var l = Ui.Weak(label);
        l.Margin = new Thickness(0);
        l.FontSize = 12.5;
        var v = Ui.Text(value);
        v.FontSize = 20;
        v.FontWeight = FontWeights.SemiBold;
        v.TextWrapping = TextWrapping.NoWrap;
        Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
        var s = new StackPanel();
        s.Children.Add(l);
        s.Children.Add(v);
        if (sub.Length > 0)
        {
            var w = Ui.Weak(sub);
            w.Margin = new Thickness(0, 2, 0, 0);
            w.FontSize = 12;
            s.Children.Add(w);
        }
        var b = new Border { Child = s, MinWidth = 118, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 16, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        b.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _tiles.Children.Add(b);
    }

    /// <summary>判定の文。線の色(成功・警告)に加えて文で言う(色だけにしない)。空なら畳む。</summary>
    void SetVerdict(string text, string brushKey)
    {
        _verdictText.Text = text;
        _verdict.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (brushKey.Length > 0)
            _verdict.SetResourceReference(Border.BorderBrushProperty, brushKey);
    }

    void ShowGuide(string title, string steps, double progress, bool stop = true)
    {
        _guide.Visibility = Visibility.Visible;
        _guideTitle.Text = title;
        Ui.SetText(_guideSteps, steps);
        _bar.Visibility = progress < 0 ? Visibility.Collapsed : Visibility.Visible;
        _barScale.ScaleX = Math.Clamp(progress, 0, 1);
        _stop.Visibility = stop ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateDeadzone()
    {
        _dzBody.Children.Clear();
        if (_drift[Right ? 1 : 0] is not DriftResult d)
        {
            _dzBody.Children.Add(Ui.Empty(Ui.Weak($"{Side}のスティックで「手を離して測る」を終えると、ずれと揺れからおすすめ値を出します")));
            return;
        }
        var a = DeadzoneAdvisor.Compute(d.MaxMagnitude, d.Noise, null);
        var big = Ui.Text(a.RecommendedPercent.ToString(Inv) + "%");
        big.FontSize = 34;
        big.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(big, FontNumeralAlignment.Tabular);
        var unit = Ui.Weak($"程度({Side}のスティック)");
        unit.Margin = new Thickness(8, 12, 0, 0);
        var why = Ui.Weak(a.Rationale);
        why.Margin = new Thickness(0, 0, 0, 8);
        Typography.SetNumeralAlignment(why, FontNumeralAlignment.Tabular);
        _dzBody.Children.Add(Ui.Horizontal(big, unit));
        _dzBody.Children.Add(why);
        foreach (var (use, hint) in a.Hints)
            _dzBody.Children.Add(Ui.Row(use, Ui.Text(hint), 120));
        var note = Ui.Weak("広げるほど触っていないときの入力は減りますが、小さな入力も効かなくなります。設定はゲームや Steam Input などで行います。PadSight はどこにも書き込みません");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 8, 0, 0);
        _dzBody.Children.Add(note);
    }

    // ---- 図

    static Ellipse Ring(double r, Brush stroke, bool dashed = false)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = 1 };
        if (dashed) e.StrokeDashArray = [3, 3];
        return e;
    }

    static void PlaceRing(Ellipse e, double r)
    {
        e.Width = e.Height = Math.Max(1, r * 2);
        e.At(C - r, C - r);
    }

    static Brush Data => Draw.Pressed(App.Current.Settings);

    bool Chart => _shown is Test.Snapback or Test.Resolution or Test.Linearity;

    /// <summary>ずれの図の拡大(中心の近くの点と、デッドゾーンの円が図に収まるように。1〜10 倍)。</summary>
    double Zoom()
    {
        if (_shown != Test.Drift)
            return 1;
        double reach = Math.Max(App.Current.Settings.Deadzone, _shownDrift?.MaxMagnitude ?? 0);
        return Math.Clamp(Math.Floor(0.9 / Math.Max(reach, 0.02)), 1, 10);
    }

    /// <summary>円・軸・デッドゾーンの円(待ちと、外周・ずれの図の地)。グラフの図では隠す。</summary>
    void DrawBase()
    {
        double z = Zoom();
        _unit.Visibility = !Chart && z == 1 ? Visibility.Visible : Visibility.Collapsed;
        _axX.Visibility = _axY.Visibility = _dz.Visibility = Chart ? Visibility.Collapsed : Visibility.Visible;
        PlaceRing(_unit, R);
        PlaceRing(_dz, App.Current.Settings.Deadzone * R * z);
        _outline.Stroke = Data;
    }

    void DrawDot(StickValue v)
    {
        _dot.Visibility = Chart ? Visibility.Collapsed : Visibility.Visible;
        if (_running && _test == Test.Drift)
            DrawDrift();
        double z = Zoom();
        double x = Math.Clamp(C + v.X * R * z, 4, Fig - 4), y = Math.Clamp(C - v.Y * R * z, 4, Fig - 4);
        _dot.At(x - 4.5, y - 4.5);
    }

    /// <summary>触っていないときの位置の散らばりを拡大して描く(平均の位置に十字、測り終えたら最大の円)。</summary>
    void DrawDrift()
    {
        _plot.Children.Clear();
        DrawBase();
        double z = Zoom();
        var cloud = new GeometryGroup();
        foreach (var p in _values.TakeLast(600))
            cloud.Children.Add(new EllipseGeometry(new Point(C + p.X * R * z, C - p.Y * R * z), 1.6, 1.6));
        _plot.Children.Add(new Path { Data = cloud, Fill = Data, Opacity = 0.75 });
        if (_shownDrift is DriftResult d && _values.Count > 0)
        {
            double mx = C + d.MeanX * R * z, my = C - d.MeanY * R * z;
            _plot.Children.Add(new Line { X1 = mx - 6, X2 = mx + 6, Y1 = my, Y2 = my, Stroke = Draw.TextBrush, StrokeThickness = 2 });
            _plot.Children.Add(new Line { X1 = mx, X2 = mx, Y1 = my - 6, Y2 = my + 6, Stroke = Draw.TextBrush, StrokeThickness = 2 });
            if (!_running)
            {
                double mr = d.MaxMagnitude * R * z;
                _plot.Children.Add(Ring(mr, Data).At(C - mr, C - mr));
            }
        }
        _plot.Children.Add(Caption(z > 1 ? $"{z.ToString("0", Inv)} 倍に拡大" : "拡大なし", 8, 6));
        _plot.Children.Add(Caption(_running ? "点線: デッドゾーンの円" : "点線: デッドゾーン  線: 最大", 8, Fig - 20));
    }

    void DrawOutline(float[] bins)
    {
        var pts = new PointCollection();
        for (int i = 0; i <= bins.Length; i++)
        {
            float m = bins[i % bins.Length];
            if (float.IsNaN(m)) continue;
            double a = (i % bins.Length * 5 + 2.5) * Math.PI / 180;
            pts.Add(new Point(C + Math.Cos(a) * m * R, C - Math.Sin(a) * m * R));
        }
        _outline.Points = pts;
    }

    static TextBlock Caption(string text, double x, double y, bool right = false)
    {
        var t = new TextBlock { Text = text, FontSize = 11, Foreground = Draw.Weak, FontFamily = Draw.Ui };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        if (right)
        {
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            x -= t.DesiredSize.Width;
        }
        return t.At(x, y);
    }

    // グラフの枠(左に目盛りの文字、下に説明の文字の場所を空ける)
    const double PL = 38, PT = 18, PR = Fig - 12, PB = Fig - 44;

    void Axes()
    {
        _plot.Children.Clear();
        _plot.Children.Add(new Line { X1 = PL, X2 = PR, Y1 = PB, Y2 = PB, Stroke = Draw.Faint });
        _plot.Children.Add(new Line { X1 = PL, X2 = PL, Y1 = PT, Y2 = PB, Stroke = Draw.Faint });
    }

    /// <summary>離した回ごとの戻る時間の棒と、その上に反対側への振れ。まだの回は点線の枠。</summary>
    void DrawSnapback(SnapbackResult r)
    {
        Axes();
        double maxMs = Math.Max(50, r.Samples.Count == 0 ? 0 : r.Samples.Max(s => s.ReturnMs) * 1.2);
        double h = PB - PT, slot = (PR - PL) / r.TargetReleases;
        _plot.Children.Add(Caption(maxMs.ToString("0", Inv), PL - 4, PT - 7, right: true));
        _plot.Children.Add(Caption("ms", PL - 4, PT + 8, right: true));
        _plot.Children.Add(Caption("0", PL - 4, PB - 8, right: true));
        for (int i = 0; i < r.TargetReleases; i++)
        {
            double x = PL + i * slot + slot * 0.22, w = slot * 0.56;
            if (i >= r.Samples.Count)
            {
                _plot.Children.Add(new Rectangle { Width = w, Height = h, Stroke = Draw.Faint, StrokeDashArray = [2, 3], RadiusX = 2, RadiusY = 2 }.At(x, PT));
                _plot.Children.Add(Caption((i + 1).ToString(Inv) + " 回目", x + w / 2 - 14, PB + 3));
                continue;
            }
            var s = r.Samples[i];
            double bh = Math.Max(2, s.ReturnMs / maxMs * h);
            _plot.Children.Add(new Rectangle { Width = w, Height = bh, Fill = Data, RadiusX = 2, RadiusY = 2 }.At(x, PB - bh));
            double oh = Math.Min(PB - bh - PT - 1, s.Overshoot * h * 0.5);
            if (s.Overshoot > 0.01f && oh > 0)
                _plot.Children.Add(new Rectangle { Width = w, Height = oh, Fill = OverBrush, RadiusX = 2, RadiusY = 2 }.At(x, PB - bh - oh - 1));
            _plot.Children.Add(Caption(s.ReturnMs.ToString("0", Inv), x + w / 2 - 6, PB + 3));
        }
        double ly = Fig - 20;
        _plot.Children.Add(new Rectangle { Width = 9, Height = 9, Fill = Data }.At(PL, ly + 4));
        _plot.Children.Add(Caption("戻るまで", PL + 13, ly));
        _plot.Children.Add(new Rectangle { Width = 9, Height = 9, Fill = OverBrush }.At(PL + 74, ly + 4));
        _plot.Children.Add(Caption("反対側への振れ", PL + 87, ly));
    }

    /// <summary>受け取った倒し具合を、縦を拡大して時間に並べる(刻みが段に見える)。</summary>
    void DrawResolution(IReadOnlyList<(double Sec, float R)> trace)
    {
        Axes();
        double secs = ResolutionNs / (double)S;
        if (trace.Count == 0)
        {
            _plot.Children.Add(Caption("動かすと、ここに値の段が出ます", PL + 8, (PT + PB) / 2 - 8));
            return;
        }
        float lo = trace.Min(p => p.R), hi = trace.Max(p => p.R);
        float pad = Math.Max((hi - lo) * 0.08f, 0.002f);
        lo = Math.Max(0, lo - pad);
        hi += pad;
        double X(double sec) => PL + Math.Min(1, sec / secs) * (PR - PL);
        double Y(float r) => PB - (r - lo) / (hi - lo) * (PB - PT);
        var pts = new PointCollection(trace.Count * 2) { new(X(trace[0].Sec), Y(trace[0].R)) };
        for (int i = 1; i < trace.Count; i++)
        {
            pts.Add(new Point(X(trace[i].Sec), Y(trace[i - 1].R)));
            pts.Add(new Point(X(trace[i].Sec), Y(trace[i].R)));
        }
        _plot.Children.Add(new Polyline { Points = pts, Stroke = Data, StrokeThickness = 1.5 });
        _plot.Children.Add(Caption(hi.ToString("0.000", Inv), PL - 3, PT - 7, right: true));
        _plot.Children.Add(Caption(lo.ToString("0.000", Inv), PL - 3, PB - 8, right: true));
        _plot.Children.Add(Caption("0 秒", PL, PB + 3));
        _plot.Children.Add(Caption($"{secs.ToString("0", Inv)} 秒", PR, PB + 3, right: true));
        _plot.Children.Add(Caption("倒し具合を拡大して時間に並べています", PL, Fig - 20));
    }

    /// <summary>倒し具合(縦)を時間(横)に並べた線と、最初と最後を結んだ直線(ここからのずれを測る)。</summary>
    void DrawLinearity(LinearityResult r, bool done)
    {
        Axes();
        double X(float t) => PL + t * (PR - PL);
        double Y(float v) => PB - Math.Clamp(v, 0, 1) * (PB - PT);
        _plot.Children.Add(new Line { X1 = PL, Y1 = PT, X2 = PR, Y2 = PT, Stroke = Draw.Faint, StrokeDashArray = [2, 3] });
        _plot.Children.Add(Caption("1.0", PL - 3, PT - 7, right: true));
        _plot.Children.Add(Caption("0", PL - 3, PB - 8, right: true));
        _plot.Children.Add(Caption("時間", PR, PB + 3, right: true));
        if (r.Curve.Count < 2)
        {
            _plot.Children.Add(Caption("倒し始めると線が出ます", PL + 8, (PT + PB) / 2 - 8));
            return;
        }
        var a = r.Curve[0];
        var b = r.Curve[^1];
        _plot.Children.Add(new Line { X1 = X(a.X), Y1 = Y(a.Y), X2 = X(b.X), Y2 = Y(b.Y), Stroke = Draw.Weak, StrokeDashArray = [4, 3] });
        var pts = new PointCollection(r.Curve.Count);
        foreach (var p in r.Curve)
            pts.Add(new Point(X(p.X), Y(p.Y)));
        _plot.Children.Add(new Polyline { Points = pts, Stroke = Data, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        _plot.Children.Add(Caption(done ? "点線: 最初と最後を結んだ直線" : "倒し具合(縦)を時間(横)に並べています", PL, Fig - 20));
    }
}
