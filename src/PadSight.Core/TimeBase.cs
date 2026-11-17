// SDL の時計(ns)を、SDL を止めて初期化し直しても前から続けて数えられるようにする(仕様書 E-29)。
// SDL は SDL_Quit → SDL_Init で時計が 0 から始まり直すので、止める直前の時刻を足しておく。
namespace PadSight.Core;

public sealed class TimeBase
{
    ulong _offset;

    /// <summary>SDL の時刻を、起動からの続きの時刻にする。</summary>
    public ulong Map(ulong sdlNs) => sdlNs + _offset;

    /// <summary>SDL を止める直前に、その時点の SDL の時刻を渡す。</summary>
    public void BeforeRestart(ulong sdlNowNs) => _offset += sdlNowNs;
}
