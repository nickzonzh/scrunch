param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$required = @('Scrunch.exe','Scrunch.dll','Scrunch.runtimeconfig.json','Microsoft.ui.xaml.dll','coreclr.dll',
    'Scrunch.pri','App.xbf','ShellView.xbf','NoteView.xbf','NoteWindow.xbf','Assets/Scrunch.ico','Assets/Fonts/InterVariable.ttf','Assets/Fonts/DrawablyPen.ttf',
    'Assets/Fonts/Inter-LICENSE.txt','Assets/Fonts/Drawably-LICENSE.txt',
    'Assets/ScrunchFX/corner-crush.nfx','Assets/ScrunchFX/side-scrunch.nfx','Assets/ScrunchFX/centre-collapse.nfx',
    'ScrunchFX/Paper.hlsl','LICENSE','THIRD_PARTY_NOTICES.md','DEPENDENCY_NOTICES.txt')
foreach ($file in $required) { if (!(Test-Path -LiteralPath (Join-Path $Directory $file))) { throw "Missing shipping file: $file" } }
$files = @(Get-ChildItem -LiteralPath $Directory -File -Recurse)
$forbidden = $files | Where-Object { $_.Name -match '(?i)(verification|product-check|startup-error|\.pdb$|\.log$|\.dmp$|\.pfx$|\.p12$|\.key$|\.cs$|\.ps1$|\.mjs$|\.mp4$|notes\.json|Caveat|Kalam|MainPage|MainWindow)' }
if ($forbidden) { throw ('Development files in payload: ' + ($forbidden.Name -join ', ')) }
# Component-only SDK deployment must not silently regain the umbrella runtime.
$unused = $files | Where-Object Name -Match '^(onnxruntime|DirectML|Microsoft\.Windows\.(AI|Widgets))'
if ($unused) { throw ('Unused SDK components in payload: ' + ($unused.Name -join ', ')) }
if (($files | Measure-Object Length -Sum).Sum -gt 200MB) { throw 'Payload exceeds the 200 MiB size budget. Review dependency or publish changes.' }
# Inspect owned managed binaries as both UTF-8 and UTF-16: a normal text search
# alone would miss .NET user strings. Vendor debug/source paths are not authored
# Scrunch paths and are left intact in signed redistributables.
$bytes = [IO.File]::ReadAllBytes((Join-Path $Directory 'Scrunch.dll'))
foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
    $text = $encoding.GetString($bytes)
    if ($text -match '(?i)C:\\Users\\|/Users/|--fx-lab|--verify-product|product-verification\.json') {
        throw 'Owned Release assembly contains a developer path or verification entrypoint.'
    }
}
[xml]$props = Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Directory.Build.props')
$info = (Get-Item (Join-Path $Directory 'Scrunch.exe')).VersionInfo
if ($info.ProductName -ne 'Scrunch' -or $info.FileVersion -ne "$($props.Project.PropertyGroup.Version).0") { throw 'Executable metadata mismatch.' }
[pscustomobject]@{ files=$files.Count; bytes=($files | Measure-Object Length -Sum).Sum; version=$info.FileVersion } | ConvertTo-Json
