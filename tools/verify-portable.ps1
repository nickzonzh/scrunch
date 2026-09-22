param()
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/native/Verify.psm1" -Force
$root = Get-RepoRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$release = Join-Path $root "artifacts/release/$version"
$evidence = Join-Path $root 'artifacts/installed-verification'
$directory = Join-Path $root ('artifacts/portable-' + [guid]::NewGuid().ToString('N'))
$data = "$directory-data"
$exe = Join-Path $directory 'Scrunch.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit Scrunch before portable verification.' }
New-Item -ItemType Directory -Force $evidence | Out-Null
Expand-Archive -LiteralPath (Join-Path $release "Scrunch-$version-win-x64.zip") -DestinationPath $directory
& "$PSScriptRoot/verify-payload.ps1" -Directory $directory
$originalData = $env:SCRUNCH_DATA_DIRECTORY
$env:SCRUNCH_DATA_DIRECTORY = $data
$app = $null; $main = $null; $errorText = $null
try {
    $started = Start-ScrunchIsolated -Executable $exe -DataDirectory $data
    $app = $started.Process; $main = $started.MainWindow
    Invoke-Ui invoke Settings -w $main | Out-Null
    $properties = (Invoke-Ui get-property StartWithWindows -w $main) -join "`n"
    if ($properties -notmatch 'ToggleState: Off' -or $properties -notmatch 'IsEnabled: False') { throw 'Portable startup is not disabled and off.' }
    Invoke-Ui inspect -w $main --depth 8 | Set-Content (Join-Path $evidence 'portable-settings.txt')
    Invoke-Ui invoke NoteFont -w $main | Out-Null
    $fontOptions = (Invoke-Ui inspect -w $main --depth 10) -join "`n"
    $inter = [regex]::Match($fontOptions, '(itm-\S+) ListItem "Inter"').Groups[1].Value
    if (!$inter) { throw 'Inter option did not appear.' }
    Invoke-Ui invoke $inter -w $main | Out-Null
    Invoke-Ui set-value NoteFontSize 28 -w $main | Out-Null
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    Invoke-Ui invoke NewNote -w $main | Out-Null
    Start-Sleep -Milliseconds 400
    $note = (Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').hwnd
    Invoke-Ui set-value NoteText 'Portable release fixture, saved locally.' -w $note | Out-Null
    Start-Sleep -Milliseconds 800
    if ((Get-Content (Join-Path $data 'notes.json') -Raw) -notmatch 'Portable release fixture') { throw 'Portable text was not saved.' }
    Invoke-Ui invoke Quit -w $main | Out-Null
    if (!$app.WaitForExit(6000)) { throw 'Portable Quit did not exit.' }
    $saved = Get-Content (Join-Path $data 'notes.json') -Raw | ConvertFrom-Json
    if ($saved.Typography.Font -ne 'inter' -or $saved.Typography.Size -ne 28) { throw 'Font settings were not saved.' }
    $started = Start-ScrunchIsolated -Executable $exe -DataDirectory $data
    $app = $started.Process; $main = $started.MainWindow
    Invoke-Ui invoke Settings -w $main | Out-Null
    $restoredFont = (Invoke-Ui get-value NoteFont -w $main) -join "`n"
    $restoredSize = (Invoke-Ui get-value InputBox -w $main) -join "`n"
    Invoke-Ui inspect -w $main --depth 8 | Set-Content (Join-Path $evidence 'portable-font-restored.txt')
    if ($restoredFont -notmatch 'Inter' -or $restoredSize -notmatch '28') { throw "Font settings were not restored after restart: font=$restoredFont; size=$restoredSize" }
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    $note = (Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').hwnd
    if (((Invoke-Ui get-value NoteText -w $note) -join "`n") -notmatch 'Portable release fixture') { throw 'Note did not restore after restart.' }
    Invoke-Ui invoke Quit -w $main | Out-Null
    if (!$app.WaitForExit(6000)) { throw 'Portable Quit after restart did not exit.' }
    Write-Host 'PASS: Exact ZIP launches, disables startup, saves a note and font settings, restores them after restart, and quits.'
} catch { $errorText = $_.ToString(); throw }
finally {
    Stop-Scrunch -Process $app -MainWindow $main -TimeoutMilliseconds 6000 | Out-Null
    $env:SCRUNCH_DATA_DIRECTORY = $originalData
    Write-Evidence (Join-Path $evidence 'portable.json') @{
        passed=($null -eq $errorText); error=$errorText
        zipHash=(Get-FileHash (Join-Path $release "Scrunch-$version-win-x64.zip")).Hash
        exeHash=(Get-FileHash $exe).Hash
        dllHash=(Get-FileHash (Join-Path $directory 'Scrunch.dll')).Hash
    }
}
