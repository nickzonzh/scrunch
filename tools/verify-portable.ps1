param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$release = Join-Path $root "artifacts/release/$version"
$evidence = Join-Path $root 'artifacts/installed-verification'
$directory = Join-Path $root ('artifacts/portable-' + [guid]::NewGuid().ToString('N'))
$data = "$directory-data"
$cli = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit Scrunch before portable verification.' }
New-Item -ItemType Directory -Force $evidence | Out-Null
Expand-Archive -LiteralPath (Join-Path $release "Scrunch-$version-win-x64.zip") -DestinationPath $directory
& "$PSScriptRoot/verify-payload.ps1" -Directory $directory
$originalData = $env:SCRUNCH_DATA_DIRECTORY
$env:SCRUNCH_DATA_DIRECTORY = $data
$app = $null; $main = $null; $errorText = $null
function Ui { $result = & $cli ui @args; if ($LASTEXITCODE) { throw "Native UI failed: $args" }; return $result }
try {
    $app = Start-Process (Join-Path $directory 'Scrunch.exe') -PassThru
    Start-Sleep -Milliseconds 1600
    $main = (Ui list-windows -a $app.Id --json | ConvertFrom-Json | Where-Object title -eq 'Scrunch').hwnd
    if (!$main) { throw 'Portable shell did not appear.' }
    Ui invoke Settings -w $main | Out-Null
    $properties = (Ui get-property StartWithWindows -w $main) -join "`n"
    if ($properties -notmatch 'ToggleState: Off' -or $properties -notmatch 'IsEnabled: False') { throw 'Portable startup is not disabled and off.' }
    Ui inspect -w $main --depth 8 | Set-Content (Join-Path $evidence 'portable-settings.txt')
    Ui invoke BackToNotes -w $main | Out-Null
    Ui invoke NewNote -w $main | Out-Null
    Start-Sleep -Milliseconds 400
    $note = (Ui list-windows -a $app.Id --json | ConvertFrom-Json | Where-Object title -eq 'Scrunch note').hwnd
    Ui set-value NoteText 'Portable release fixture, saved locally.' -w $note | Out-Null
    Start-Sleep -Milliseconds 800
    if ((Get-Content (Join-Path $data 'notes.json') -Raw) -notmatch 'Portable release fixture') { throw 'Portable text was not saved.' }
    Ui invoke Quit -w $main | Out-Null
    if (!$app.WaitForExit(6000)) { throw 'Portable Quit did not exit.' }
    Write-Host 'PASS: Exact ZIP launches, disables startup registration, creates/edits/saves a note and quits.'
} catch { $errorText = $_.ToString(); throw }
finally {
    if ($app -and !$app.HasExited -and $main) {
        if (((Ui inspect -w $main) -join "`n") -match 'Back to notes') { Ui invoke BackToNotes -w $main | Out-Null }
        Ui invoke Quit -w $main | Out-Null
        $app.WaitForExit(6000) | Out-Null
    }
    $env:SCRUNCH_DATA_DIRECTORY = $originalData
    @{ passed=($null -eq $errorText); error=$errorText; zipHash=(Get-FileHash (Join-Path $release "Scrunch-$version-win-x64.zip")).Hash;
       exeHash=(Get-FileHash (Join-Path $directory 'Scrunch.exe')).Hash; dllHash=(Get-FileHash (Join-Path $directory 'Scrunch.dll')).Hash } |
        ConvertTo-Json | Set-Content (Join-Path $evidence 'portable.json')
}
