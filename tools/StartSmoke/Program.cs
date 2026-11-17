// 起動の最初の 2 歩(App.Main と同じ new App → InitializeComponent)だけを試し、落ちたら終了コード 1 で知らせる。
// 窓・トレイ・ホットキーは作らない(Run しない。OnStartup も動かない)。使い方: dotnet run --project tools\StartSmoke
// 2026-11-17 に、部品の初期値が静的な初期化を早めに走らせ、配布版が起動の直後に落ちた。その形の失敗をここで止める。
int code = 1;
var t = new Thread(() =>
{
    try
    {
        var app = new PadSight.App.App();
        app.InitializeComponent();
        Console.WriteLine("start-smoke: ok");
        code = 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("start-smoke: 起動の最初で落ちました");
        Console.WriteLine(ex);
    }
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
return code;
