# publish.ps1 - publish PadSight.App as a self-contained single-file win-x64 exe and zip it (spec 9.1).
# Output: dist\PadSight\ and dist\PadSight-v<Version>-win-x64.zip, Version taken from Directory.Build.props.
# Keep this file ASCII only (Windows PowerShell 5.1 reads BOM-less files in the ANSI code page).
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propsPath = Join-Path $root 'Directory.Build.props'
[xml]$props = [System.IO.File]::ReadAllText($propsPath)
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> in $propsPath" }

$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'PadSight'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }

$project = Join-Path $root 'src\PadSight.App\PadSight.App.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$items = @(Get-ChildItem -LiteralPath $out | ForEach-Object { $_.FullName })
foreach ($doc in @('README.md', 'README.en.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md')) {
    $path = Join-Path $root $doc
    if (Test-Path -LiteralPath $path) { $items += $path }
}

$zip = Join-Path $dist ('PadSight-v{0}-win-x64.zip' -f $version)
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $items -DestinationPath $zip
Write-Output "published: $zip"
