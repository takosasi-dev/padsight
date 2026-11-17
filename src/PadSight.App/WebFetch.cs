// 通信はこのファイルだけ(仕様書 INV-9・§9.17)。宛先は 2 つの URL に固定し、送るのは決まった見出しだけ。
// パッドの情報・統計・設定・ログは送らない。tools\check.ps1 が、ここ以外での HttpClient を止める。
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace PadSight.App;

public static class WebFetch
{
    public const string MappingUrl = "https://raw.githubusercontent.com/mdqinc/SDL_GameControllerDB/master/gamecontrollerdb.txt";
    public const string LatestReleaseUrl = "https://api.github.com/repos/takosasi-dev/padsight/releases/latest";

    static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"PadSight/{App.Version}");
        return c;
    }

    /// <summary>結果。Text が null なら Error に理由(画面に出す短い日本語)。NotFound は 404。</summary>
    public sealed record Result(string? Text, string? Error, bool NotFound = false);

    public static Task<Result> Mapping() => Get(MappingUrl, PadSight.Core.MappingDb.MaxBytes, null);

    public static Task<Result> LatestRelease() => Get(LatestReleaseUrl, 256 * 1024, "application/vnd.github+json");

    static async Task<Result> Get(string url, int maxBytes, string? accept)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept is not null)
                req.Headers.Accept.ParseAdd(accept);
            using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return new(null, "見つかりませんでした", NotFound: true);
            if (!res.IsSuccessStatusCode)
                return new(null, $"サーバーの答えが {(int)res.StatusCode} でした");
            await using var stream = await res.Content.ReadAsStreamAsync();
            var buf = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int n;
            while ((n = await stream.ReadAsync(chunk)) > 0)
            {
                buf.Write(chunk, 0, n);
                if (buf.Length > maxBytes)
                    return new(null, "大きすぎます");
            }
            return new(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buf.ToArray()), null);
        }
        catch (TaskCanceledException) { return new(null, "時間内に答えがありませんでした"); }
        catch (HttpRequestException ex) { AppLog.Write("通信の失敗: " + ex.Message); return new(null, "つながりませんでした"); }
        catch (DecoderFallbackException) { return new(null, "文字として読めませんでした"); }
        catch (IOException ex) { AppLog.Write("通信の失敗: " + ex.Message); return new(null, "途中で切れました"); }
    }
}
