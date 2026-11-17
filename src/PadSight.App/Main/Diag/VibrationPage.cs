// 診断の「振動」のページ(仕様書 FR-58・D-28・§9.20、画面設計書 §4.7)。押したときだけ App.Vibrate で 0.5 秒の振動を送る(止めるのは SDL)。自動では送らない。
// 左・右・両方のモーター × 弱・強、対応するパッドではトリガーのモーターも。感じたかどうかは利用者が答え、「この結果を残す」で記録に残す(Kind vibration)。
// 振動できないパッド・パッドが無いときは押せなくし、理由を出す。Bluetooth の PlayStation のパッドには注意の文を出す。
using System.Windows;
using System.Windows.Controls;
using PadSight.Core;

namespace PadSight.App.Main.Diag;

public sealed class VibrationPage : IDiagPage
{
    const ushort Weak = 26214, Strong = 65535; // 40% と 100%

    /// <summary>1 つのモーター(か、両方)の行。Ask が真の行だけ、感じたかを聞く。</summary>
    sealed record Motor(string Name, string Sub, bool Triggers, bool LeftOn, bool RightOn, bool Ask);

    static readonly Motor[] Motors =
    [
        new("左のモーター", "重く低い振動", false, true, false, true),
        new("右のモーター", "軽く高い振動", false, false, true, true),
        new("両方のモーター", "", false, true, true, false),
        new("左のトリガー", "LT の奥の振動", true, true, false, true),
        new("右のトリガー", "RT の奥の振動", true, false, true, true),
    ];

    readonly TextBlock _reason = Ui.Text();
    readonly TextBlock _btNote = Ui.Text();
    readonly TextBlock _trigNote = Ui.Weak("トリガーの振動は、対応しているパッド(Xbox 系)だけで試せます");
    readonly TextBlock _sent = Ui.Weak();
    readonly Grid _table = new() { Margin = new Thickness(0, 6, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
    readonly List<(Motor M, UIElement[] Cells, Button[] Sends, Button[] Answers, TextBlock Answer)> _rows = [];
    readonly Dictionary<string, bool> _felt = [];
    readonly HashSet<string> _tried = [];
    readonly Button _keep;
    readonly TextBlock _kept = Ui.Weak();
    string _padKey = "?";
    PadInfo? _pad;

    public VibrationPage()
    {
        for (int i = 0; i < 5; i++)
            _table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var m in Motors) AddRow(m);

        var lead = Ui.Text("押したときだけ、パッドに振動を送ります");
        lead.FontWeight = FontWeights.SemiBold;
        _reason.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        _reason.Margin = new Thickness(0, 6, 0, 2);
        _btNote.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        _btNote.Margin = new Thickness(0, 6, 0, 2);
        _sent.MinHeight = 22;
        Ui.SetText(_btNote, "");
        _keep = Ui.Button("この結果を残す", Keep);
        _kept.Margin = new Thickness(4, 0, 0, 0);

        var card = Ui.Card("振動",
            lead,
            Ui.Weak("1 回押すと 0.5 秒だけ振動して止まります。自動で振動させることはありません。弱は 40%、強は 100% の強さです"),
            _reason,
            _btNote,
            _table,
            _trigNote,
            _sent);
        var result = Ui.Card("振動を感じましたか",
            Ui.Text("振動を送ったら、手に伝わったかどうかを [感じた] か [感じない] で答えてください。PadSight からは、パッドが実際に振動したかは分かりません"),
            Ui.Horizontal(_keep, _kept),
            Ui.Weak("感じなかったときは、モーターの不調のほか、つなぎ方やパッドの設定で振動が届いていない可能性もあります。判定は目安です"));
        View = TriggerPage.PageScroll(card, result);
        Apply(null);
    }

    public string Title => "振動";
    public FrameworkElement View { get; }
    public bool WantsSamples => false;

    public void Render(DiagContext c)
    {
        var p = c.Pad;
        string key = p is null ? "" : $"{p.InstanceId}|{p.CanVibrate}|{p.CanVibrateTriggers}|{p.Connection}";
        if (key == _padKey) return;
        bool samePad = p is not null && _pad is not null && p.InstanceId == _pad.InstanceId;
        _padKey = key;
        _pad = p;
        if (!samePad)
        {
            _felt.Clear();
            _tried.Clear();
            Ui.SetText(_sent, "");
        }
        Apply(p);
    }

    public void Cancel() { } // 振動は SDL が 0.5 秒で止める。ここで送る物は無い

    void Apply(PadInfo? p)
    {
        string reason = p is null ? "パッドが見つかりません。パッドをつなぐと試せます"
            : !p.CanVibrate ? "このパッドは振動に対応していないと SDL が答えています(振動のモーターが無いか、今のつなぎ方では使えない可能性があります)。振動のボタンは押せません"
            : "";
        Ui.SetText(_reason, reason);
        bool btPs = p is { Connection: "Wireless", Kind: PadKind.PlayStation };
        Ui.SetText(_btNote, btPs ? "Bluetooth の PlayStation のパッドは振動しないことがあります(他のアプリを壊さないための設定です)" : "");
        bool motors = p?.CanVibrate == true, triggers = p?.CanVibrateTriggers == true;
        string lt = "LT", rt = "RT";
        if (p is not null)
        {
            var settings = App.Current.Settings;
            var skin = Labels.ChooseSkin(p, settings.Devices.GetValueOrDefault(p.Guid));
            lt = Labels.For("LeftTrigger", skin, settings.LabelStyle, p);
            rt = Labels.For("RightTrigger", skin, settings.LabelStyle, p);
        }
        foreach (var (m, cells, sends, answers, _) in _rows)
        {
            bool shown = !m.Triggers || triggers;
            foreach (var cell in cells) cell.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (m.Triggers && cells[0] is StackPanel { Children: [_, TextBlock sub] })
                sub.Text = $"{(m.LeftOn ? lt : rt)} の奥の振動";
            foreach (var b in sends) b.IsEnabled = m.Triggers ? triggers : motors;
            foreach (var b in answers) b.IsEnabled = _tried.Contains(m.Name);
        }
        _trigNote.Visibility = p is not null && motors && !triggers ? Visibility.Visible : Visibility.Collapsed;
        RefreshAnswers();
    }

    void AddRow(Motor m)
    {
        int row = _table.RowDefinitions.Count;
        _table.RowDefinitions.Add(new RowDefinition());
        var name = Ui.Text(m.Name);
        name.FontWeight = FontWeights.SemiBold;
        var label = new StackPanel { Width = 170, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
        label.Children.Add(name);
        var sub = Ui.Weak(m.Sub);
        sub.Margin = new Thickness(0);
        sub.FontSize = 12;
        label.Children.Add(sub);
        if (m.Sub.Length == 0) sub.Visibility = Visibility.Collapsed;
        var weak = Ui.Button("弱", () => Send(m, false));
        var strong = Ui.Button("強", () => Send(m, true));
        weak.MinWidth = strong.MinWidth = 64;
        System.Windows.Automation.AutomationProperties.SetName(weak, m.Name + "を弱く振動させる");
        System.Windows.Automation.AutomationProperties.SetName(strong, m.Name + "を強く振動させる");
        var sends = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        sends.Children.Add(weak);
        sends.Children.Add(strong);
        var answers = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var answer = Ui.Text();
        answer.MinWidth = 90;
        answer.Margin = new Thickness(8, 0, 0, 0);
        Button[] answerButtons = [];
        if (m.Ask)
        {
            var yes = Ui.Button("感じた", () => Answer(m, true));
            var no = Ui.Button("感じない", () => Answer(m, false));
            System.Windows.Automation.AutomationProperties.SetName(yes, m.Name + "の振動を感じた");
            System.Windows.Automation.AutomationProperties.SetName(no, m.Name + "の振動を感じない");
            answers.Children.Add(yes);
            answers.Children.Add(no);
            answerButtons = [yes, no];
        }
        UIElement[] cells = [label, sends, answers, answer];
        for (int col = 0; col < cells.Length; col++)
        {
            Grid.SetRow(cells[col], row);
            Grid.SetColumn(cells[col], col);
            _table.Children.Add(cells[col]);
        }
        // 行の間の薄い線(最初の行の上には引かない)
        if (row > 0)
        {
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            line.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 5);
            _table.Children.Add(line);
            cells = [.. cells, line];
        }
        _rows.Add((m, cells, [weak, strong], answerButtons, answer));
    }

    /// <summary>利用者が押したときだけ呼ばれる(INV-3 の例外。D-28)。</summary>
    void Send(Motor m, bool strong)
    {
        if (_pad is not { CanVibrate: true } pad || (m.Triggers && !pad.CanVibrateTriggers)) return;
        ushort v = strong ? Strong : Weak;
        App.Current.Vibrate(pad.InstanceId, m.LeftOn ? v : (ushort)0, m.RightOn ? v : (ushort)0, m.Triggers);
        // 両方を試したら、左右のどちらにも感じたかを聞けるようにする
        foreach (var r in Motors)
            if (r.Ask && r.Triggers == m.Triggers && ((m.LeftOn && r.LeftOn) || (m.RightOn && r.RightOn)))
                _tried.Add(r.Name);
        Ui.SetText(_sent, $"{m.Name}に{(strong ? "強い" : "弱い")}振動を 0.5 秒送りました");
        Apply(pad);
    }

    void Answer(Motor m, bool felt)
    {
        _felt[m.Name] = felt;
        RefreshAnswers();
    }

    void RefreshAnswers()
    {
        foreach (var (m, _, _, _, answer) in _rows)
        {
            bool has = _felt.TryGetValue(m.Name, out bool felt);
            answer.Text = !has ? "" : felt ? "感じた" : "感じない";
            answer.FontWeight = has && !felt ? FontWeights.SemiBold : FontWeights.Normal;
            answer.SetResourceReference(TextBlock.ForegroundProperty, has && !felt ? "WarningTextBrush" : "TextBrush");
        }
        _keep.IsEnabled = _pad is not null && _felt.Count > 0;
        Ui.SetText(_kept, "");
    }

    void Keep()
    {
        if (_pad is not { } pad || _felt.Count == 0) return;
        var answered = Motors.Where(m => _felt.ContainsKey(m.Name)).ToList();
        var notFelt = answered.Where(m => !_felt[m.Name]).Select(m => m.Name).ToList();
        string verdict = notFelt.Count == 0
            ? "答えたモーターは、どれも振動を感じられました"
            : $"{string.Join("・", notFelt)}の振動を感じられなかったとのことです。モーターの不調、または今のつなぎ方では振動が届いていない可能性があります";
        var rec = new DiagRecord("vibration", "振動", DateTime.UtcNow, pad.Name, pad.Kind.ToString(),
            [.. answered.Select(m => new KeyValuePair<string, double>(m.Name + "(感じた=1)", _felt[m.Name] ? 1 : 0))],
            verdict, [], App.Version);
        bool ok = App.Current.Diagnostics.Save(rec) is not null;
        Ui.SetText(_kept, ok ? "残しました" : "残せませんでした(ログを見てください)");
        _keep.IsEnabled = !ok;
    }
}
