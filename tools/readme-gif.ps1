# README の動く絵(docs\images\panels.gif)を作り直す。窓は出さない。ffmpeg が要る。
# デモの入力でパネル 4 枚を動かした 12 秒(25 fps)を UiPerfProbe で PNG にして、ffmpeg で GIF にする。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$frames = Join-Path ([IO.Path]::GetTempPath()) 'PadSightGifFrames'
if (Test-Path $frames) { Remove-Item -Recurse -Force $frames }
dotnet run -c Release --project (Join-Path $root 'tools\UiPerfProbe') -- gif $frames
if ($LASTEXITCODE -ne 0) { throw 'UiPerfProbe が失敗しました' }
$out = Join-Path $root 'docs\images\panels.gif'
# 色は 256 色に減らす(全部のフレームから 1 つの表を作る)。動いた所だけを書き換えて小さくする
ffmpeg -y -loglevel error -framerate 25 -i (Join-Path $frames '%03d.png') -vf 'split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle' -loop 0 $out
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg が失敗しました' }
'{0}  {1:N0} KB' -f $out, ((Get-Item $out).Length / 1KB)
