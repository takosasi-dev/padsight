# check.ps1 - forbidden-string search for PadSight (spec 9.12 / NFR-10, INV-11, INV-16).
# Scans src, tests and tools under the repo root (bin and obj skipped) and prints "file:line: pattern".
# Exit 1 if anything is found, else prints "check: 0 hits" and exits 0. Keep this file ASCII only.
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$self = $PSCommandPath

# Plain substrings, matched case-insensitively. This file is the only place they may appear.
$banned = @(
    'OpenProcess', 'ReadProcessMemory', 'WriteProcessMemory', 'CreateRemoteThread',
    'System.Diagnostics.Process', 'GetProcesses', 'CreateToolhelp32Snapshot',
    'SetWindowsHookEx', 'BitBlt', 'CopyFromScreen', 'PrintWindow', 'Windows.Graphics.Capture',
    'IDXGI', 'D3D11', 'D3D12', 'Vulkan',
    'Rumble', 'SetGamepadLED', 'SetJoystickLED', 'SendGamepadEffect', 'SendJoystickEffect',
    'SensorEnabled', 'PlayerIndex', 'AttachVirtualJoystick', 'ALLOW_STEAM_VIRTUAL_GAMEPAD',
    'SendInput', 'keybd_event', 'mouse_event', 'ViGEm',
    'GetAsyncKeyState', 'GetKeyboardState', 'RegisterRawInputDevices', 'SDL_INIT_VIDEO',
    'FindWindow', 'EnumWindows', 'GetForegroundWindow', 'SetForegroundWindow', 'WindowFromPoint',
    'GetWindowThreadProcessId',
    'HttpClient', 'Socket', 'WebClient', 'WebRequest'
)

# INV-11: native imports are allowed only in these files (paths relative to the repo root).
$importAllowed = @(
    'src\OverlayCore\Win32Window.cs',
    'src\OverlayCore\MonitorInfo.cs',
    'src\OverlayCore\HotkeyHost.cs',
    'src\PadSight.App\ShellOpen.cs',
    'tools\OverlayProbe\Win32Window.cs'
)

$extensions = @('.cs', '.xaml', '.csproj', '.props', '.ps1', '.json', '.txt')
$ic = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase

function New-Rule([string]$name, [string]$pattern) {
    New-Object PSObject -Property @{ Name = $name; Rx = New-Object System.Text.RegularExpressions.Regex($pattern, $ic) }
}

$always = @($banned | ForEach-Object { New-Rule $_ ([regex]::Escape($_)) })
$always += New-Rule 'pragma warning disable RS0030' 'pragma\s+warning\s+disable.*RS0030'
$importRules = @('DllImport', 'LibraryImport' | ForEach-Object { New-Rule $_ ([regex]::Escape($_)) })
$overlayCoreRule = New-Rule 'PadSight (INV-16)' 'PadSight'
$projectRefRule = New-Rule 'ProjectReference (INV-16)' 'ProjectReference'

$hits = New-Object System.Collections.Generic.List[string]

foreach ($dir in @('src', 'tests', 'tools')) {
    $base = Join-Path $root $dir
    if (-not (Test-Path -LiteralPath $base)) { continue }

    $files = Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object {
        $rel = $_.FullName.Substring($root.Length)
        ($extensions -contains $_.Extension.ToLowerInvariant()) -and
        ($_.FullName -ne $self) -and
        -not ($rel -match '\\(bin|obj)\\')
    }

    foreach ($file in $files) {
        $rel = $file.FullName.Substring($root.Length).TrimStart('\')
        $rules = $always
        if (-not ($importAllowed -contains $rel)) { $rules += $importRules }
        if ($rel -like 'src\OverlayCore\*') { $rules += $overlayCoreRule }
        if ($rel -eq 'src\OverlayCore\OverlayCore.csproj') { $rules += $projectRefRule }

        # ReadAllLines decodes BOM-less files as UTF-8 (Get-Content in PS 5.1 would use the ANSI code page).
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        for ($i = 0; $i -lt $lines.Length; $i++) {
            foreach ($rule in $rules) {
                if ($rule.Rx.IsMatch($lines[$i])) { $hits.Add(('{0}:{1}: {2}' -f $rel, ($i + 1), $rule.Name)) }
            }
        }
    }
}

if ($hits.Count -gt 0) {
    $hits | ForEach-Object { Write-Output $_ }
    Write-Output ('check: {0} hits' -f $hits.Count)
    exit 1
}
Write-Output 'check: 0 hits'
exit 0
