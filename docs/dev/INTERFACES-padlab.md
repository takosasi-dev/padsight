# PadLab の統合 — 担当の間の取り決め

仕様書: `<vault>\仕様書MDファイル\Windows\ゲーム攻略・支援ツール\PadSight\PadSight_仕様書.md` §9.20・FR-54〜FR-60・AC-67〜AC-72・D-28。画面設計書 §4.7(診断)。
移す元: `L:\PadLab`(読むだけ。書き換えない)。判定の考え方・閾値・文言(断定しない)は PadLab の `src\PadLab.Core\Analysis\**`・`src\PadLab.App\Pages\**`・`src\PadLab.App\Strings.cs`・`tests\PadLab.Core.Tests\**` から移す。

## 土台(main の dc93f83 で入っている。変えない)

- `PadSight.Core.DiagSample(ulong Ns, uint PadId, uint Buttons, StickValue Left, StickValue Right, float LeftTrigger, float RightTrigger)` — PadLab の `GamepadSnapshot` の代わり。`Has(PadButton)`。スティックの Y は上が正(`StickValue`)。時刻は SDL の ns(デモのときは Stopwatch の ns)。
- `PadSight.App.Main.IDiagPage` / `DiagContext(S, NowNs, Pad, Profile, Samples)` — `Samples` は前の描画から今までの、選んだパッドの分(古い順)。`WantsSamples` が真の間だけ積まれる。測っている間だけ真にするのが望ましい。
- `PadSight.Core.PadProfiles` / `PadProfile(Id, Name, Buttons, PaddleCount)` / `ProfileButton(Key, Label, Extended)` — Key は PadButton の名前か `LeftTrigger`・`RightTrigger`。
- `App.Current.Diagnostics.Save(DiagRecord)` / `.List()` / `.Delete(path)` — 結果を残す。`DiagRecord(Kind, Title, AtUtc, PadName, PadKind, Values, Verdict, Shape, AppVersion)`。時刻つきの入力は入れない(INV-8)。`AppVersion` は `App.Version`。
- `App.Current.Vibrate(padId, low, high, triggers)` — 振動テストだけが使う(D-28)。`PadInfo.CanVibrate`・`CanVibrateTriggers`・`Connection`(`Wired`/`Wireless`/`Unknown`)。
- 画面の部品は `Main\Ui.cs`(Card・Row・Stat・Column・Empty・Picture・Primary は使わない(主なボタンはホームだけ))と `App.xaml` のスタイル。色は DynamicResource。

## 担当とファイル

各担当は自分のファイルだけを触る。`DiagnosticsTab.cs` の `Pages` の並びは、**自分のページの 1 行だけ**を差し替える(ほかの行は触らない)。

| 担当 | 作る | 触ってよいファイル |
|---|---|---|
| B1 ボタン | ボタンの網羅(プロファイルの全部、押した・離した)、パドルの確認、ボタンの検査(FR-45。今の `DiagTab.cs` のボタンの検査の節を、こちらへ写して作り直す) | 新 `src\PadSight.App\Main\Diag\ButtonsPage.cs`、新 `src\PadSight.Core\Diag\ButtonCoverage.cs`・`PaddleMapper.cs`、新 `tests\PadSight.Tests\DiagButtonsTests.cs`、`DiagnosticsTab.cs` の Pages の「ボタン」の行 |
| B2 スティック | 今の `DiagTab.cs` を「スティック」のページに作り直す(パッドの選択は外枠の物を使う。ボタンの検査とトリガーの範囲の節は**消す**。B1・B3 が写す)。スナップバック・分解能・応答カーブ・デッドゾーンのおすすめ値・外周(PadLab の StickRange と今の外周の合わせ方は任せる) | `src\PadSight.App\Main\DiagTab.cs`(名前を `StickPage` 相当に変えてよい。`DiagnosticsTab.cs` の中の仮の `StickPage` クラスは消して自分の物に)、新 `src\PadSight.Core\Diag\Snapback.cs`・`Resolution.cs`・`Linearity.cs`・`DeadzoneAdvisor.cs`(と要るなら `StickRange.cs`)、`src\PadSight.Core\StickDiagnostics.cs`、新 `tests\PadSight.Tests\DiagStickTests.cs`、`DiagnosticsTab.cs` の Pages の「スティック」の行と仮の StickPage |
| B3 トリガー・振動・放置監視 | トリガーの時系列(直近 5 秒)とトリガーの範囲(FR-46。今の `DiagTab.cs` の節を写して作り直す)、振動テスト(FR-58)、放置監視(FR-59。PadLab の IdleMonitor。報告の途切れは SDL では分からないので、切断・再接続はスナップショットの `Pads` で見る) | 新 `src\PadSight.App\Main\Diag\TriggerPage.cs`・`VibrationPage.cs`・`IdlePage.cs`、新 `src\PadSight.Core\Diag\TriggerTrace.cs`・`IdleMonitor.cs`、新 `tests\PadSight.Tests\DiagTriggerIdleTests.cs`、`DiagnosticsTab.cs` の Pages の「トリガー」「振動」「放置監視」の行 |
| B4 履歴・レポート | 履歴のページ(一覧・消す・レポートの画像を書き出す。PadLab の ReportCard を WPF で描いて PNG に。SkiaSharp は使わない)、報告の間隔の参考値(PadLab の PollingRateMeter。外枠の情報の行の末尾に足す) | 新 `src\PadSight.App\Main\Diag\HistoryPage.cs`・`ReportCard.cs`、新 `src\PadSight.Core\Diag\PollingRate.cs`、新 `tests\PadSight.Tests\DiagHistoryTests.cs`、`DiagnosticsTab.cs` の Pages の「履歴」の行と `Info(...)` の中だけ(報告の間隔を足す。DiagSample を外枠で数えてよい) |

## 共通の決まり

- 各ページの結果の下に「この結果を残す」(残したら弱い文字で「残しました」)。`Kind` は `buttons`・`paddles`・`chatter`・`stick_drift`・`stick_outer`・`snapback`・`resolution`・`linearity`・`trigger`・`vibration`・`idle`。
- 判定の文は PadLab と同じく断定しない(「〜の可能性があります」「目安です」)。
- 新しいファイルの先頭に 3 行以内の日本語の責務のコメント。Core は WPF・ファイル・`DateTime.Now` を使わない(INV-13)。
- PadLab のテストの値を移して、同じ数が出ることを確かめる(AC-69)。
- 窓を出さない(PadSight.exe を起動しない)。見た目は画面に出さずに PNG に描いて確かめる(例 `scratchpad\w5shots\Program.cs`: `new App()` + `InitializeComponent()` を `Run` せずに使う)。
- `git stash` を使わない。push しない。ほかの worktree を触らない。
- 終わる前に `dotnet build PadSight.slnx -warnaserror`(警告 0)・`dotnet test tests/PadSight.Tests/PadSight.Tests.csproj`(全部成功)・`powershell -ExecutionPolicy Bypass -File tools\check.ps1`(0 件)。作者 `takosasi-dev <191810276+takosasi-dev@users.noreply.github.com>`、メッセージの最後に `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
