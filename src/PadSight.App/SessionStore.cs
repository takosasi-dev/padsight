// sessions.jsonl と current_session.json の読み書き(仕様書 FR-25・E-14〜E-16)。書くのは専用のスレッド 1 本。
// 確定した行は追記だけで書き換えない。書けなかった行はメモリに残し、次の確定のときにもう一度書く。
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using PadSight.Core;

namespace PadSight.App;

public sealed class SessionStore : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);
    readonly string _dir;
    readonly BlockingCollection<Action> _queue = [];
    readonly Thread _thread;
    readonly List<string> _pending = [];

    public SessionStore(string dataDir)
    {
        _dir = dataDir;
        _thread = new Thread(() => { foreach (var a in _queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "SessionStore" };
        _thread.Start();
    }

    public string SessionsPath => Path.Combine(_dir, "sessions.jsonl");
    string CurrentPath => Path.Combine(_dir, "current_session.json");

    /// <summary>保存に失敗した・直った。UI のスレッドとは限らない。</summary>
    public event Action<bool>? SaveFailedChanged;

    /// <summary>確定した行が足された。</summary>
    public event Action? Appended;

    /// <summary>起動時に、前回の進行中のセッションを、最後の操作の時刻で確定して足す(FR-25・E-16)。</summary>
    public void RecoverCurrent()
    {
        if (!File.Exists(CurrentPath))
            return;
        var rec = SessionJson.Parse(File.ReadAllText(CurrentPath, Encoding.UTF8));
        if (rec is { DurationSeconds: > 0 })
            Append([rec with { LastOperationUtc = null }]);
        _queue.Add(() => TryIo(() => File.Delete(CurrentPath)));
        AppLog.Write(rec is null ? "読めない current_session.json を消した" : "前回のセッションを確定した");
    }

    public void Append(IReadOnlyList<SessionRecord> records)
    {
        var lines = records.Select(r => SessionJson.ToLine(r with { LastOperationUtc = null }, TimeZoneInfo.Local)).ToList();
        _queue.Add(() =>
        {
            _pending.AddRange(lines);
            bool ok = TryIo(() => File.AppendAllText(SessionsPath, string.Concat(_pending.Select(l => l + "\n")), Utf8));
            if (ok)
            {
                _pending.Clear();
                TryIo(() => File.Delete(CurrentPath));
                Appended?.Invoke();
            }
            SaveFailedChanged?.Invoke(!ok);
        });
    }

    /// <summary>進行中のセッションを 60 秒ごとに書き換える(§9.6.4)。</summary>
    public void WriteCurrent(SessionRecord r)
    {
        string json = SessionJson.ToLine(r, TimeZoneInfo.Local);
        _queue.Add(() => TryIo(() =>
        {
            File.WriteAllText(CurrentPath + ".tmp", json, Utf8);
            File.Move(CurrentPath + ".tmp", CurrentPath, overwrite: true);
        }));
    }

    /// <summary>全部の行を読む。読めなかった行の数も返す(E-14)。</summary>
    public (List<SessionRecord> Records, int Bad) ReadAll()
    {
        var list = new List<SessionRecord>();
        int bad = 0;
        if (!File.Exists(SessionsPath))
            return (list, 0);
        string[] lines;
        try
        {
            lines = File.ReadAllLines(SessionsPath, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            AppLog.Error("sessions.jsonl の読み込み", ex);
            return (list, 0);
        }
        foreach (var line in lines.Where(l => l.Trim().Length > 0))
        {
            if (SessionJson.Parse(line) is { } r) list.Add(r);
            else bad++;
        }
        return (list, bad);
    }

    bool TryIo(Action a)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            a();
            return true;
        }
        catch (IOException ex) { AppLog.Error("統計の保存", ex); }
        catch (UnauthorizedAccessException ex) { AppLog.Error("統計の保存", ex); }
        return false;
    }

    /// <summary>残っている書き込みを終えてから止める。</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }
}
