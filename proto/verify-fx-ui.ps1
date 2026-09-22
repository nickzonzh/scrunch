param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/scrunchfx-ui'))
$ErrorActionPreference = 'Stop'
$cli = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$checks = [System.Collections.Generic.List[string]]::new()
function Ui {
    $result = & $cli ui @args
    if ($LASTEXITCODE -ne 0) { throw "WinApp failed: $args`n$result" }
    return $result
}
function Windows { return (Ui list-windows -a $AppPid --json | ConvertFrom-Json) }
$main = (Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if (!$main) { throw 'Launch a fresh DEBUG --fx-lab session. This script refuses everyday notes.' }
if (Windows | Where-Object title -eq 'Scrunch note') { throw 'Use a fresh FX lab with no existing test notes.' }
function Assert-Editor([long]$window, [string]$expected) {
    $actual = (Ui get-value NoteText -w $window --json | ConvertFrom-Json).text
    # Native TextBox TextPattern uses CR paragraph delimiters, while the input
    # command uses LF. Compare the content, not the transport's line endings.
    if (($actual -replace "`r`n?", "`n") -ne ($expected -replace "`r`n?", "`n")) { throw 'Native editor value differs' }
}
try {
    Ui invoke NewNote -w $main | Out-Null
    $note = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    $text = "SCRUNCH FX / 21 SEPTEMBER`nActual native ink.`n`nRemember the green notebook."
    Ui set-value NoteText $text -w $note | Out-Null
    Assert-Editor $note $text
    $checks.Add('Created a real native note; editor value matches identifying text')
    Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory '00-lab.png') | Out-Null
    Ui screenshot -w $note --capture-screen -o (Join-Path $OutputDirectory '01-note.png') | Out-Null
    Ui invoke FxHold -w $main | Out-Null
    Ui set-value FxProgress 0 -w $main | Out-Null
    Ui invoke FxDelete -w $main | Out-Null
    $overlay = $null
    for ($i=0; $i -lt 30 -and !$overlay; $i++) {
        Start-Sleep -Milliseconds 100
        $overlay = (Windows | Where-Object title -eq 'ScrunchFX paper').hwnd
    }
    if (!$overlay) { throw 'No native D3D overlay appeared' }
    $index = 2
    foreach ($progress in @(0, .3, .6, 1)) {
        Ui set-value FxProgress $progress -w $main | Out-Null
        Start-Sleep -Milliseconds 200
        Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory 'lab-overview.png') | Out-Null
        Ui screenshot -w $overlay --capture-screen -o (Join-Path $OutputDirectory ("{0:00}-pose-{1}.png" -f $index,$progress)) | Out-Null
        $index++
    }
    $checks.Add('Native overlay present; debug slider accepts 0, 0.3, 0.6, 1; screenshots saved for visual inspection')
    Ui invoke 'Undo last discard' -w $main | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Editor $note $text
    $checks.Add('Undo during held playback restores the same editable note with its text')
    Ui invoke FxHold -w $main | Out-Null
    Ui invoke FxSlow -w $main | Out-Null
    Ui invoke FxDelete -w $main | Out-Null
    Start-Sleep -Milliseconds 1300
    if (Windows | Where-Object hwnd -eq $note) { throw 'Deleted note window remains' }
    if (Windows | Where-Object title -eq 'ScrunchFX paper') { throw 'FX overlay remains visible after completion' }
    $checks.Add('Normal-speed delete closes the real note and hides the overlay')
    Ui invoke 'Undo last discard' -w $main | Out-Null
    $restored = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    Ui set-value NoteText 'Editing still works after ScrunchFX.' -w $restored | Out-Null
    Assert-Editor $restored 'Editing still works after ScrunchFX.'
    $checks.Add('Completed discard remains recoverable; normal editing works afterward')
    # A focused native TextBox can consume Delete before parent accelerators.
    # Exercise actual key delivery, not a direct call to the discard method.
    Ui click NoteText -w $restored | Out-Null
    Ui send-keys 'ctrl+shift+delete' -w $restored --via send-input | Out-Null
    Start-Sleep -Milliseconds 150
    if ((Windows | Where-Object hwnd -eq $restored) -or (Windows | Where-Object title -eq 'ScrunchFX paper')) { throw 'Keyboard discard did not close immediately' }
    $checks.Add('Ctrl+Shift+Delete from the focused native editor closes without an FX overlay')
    Ui invoke 'Undo last discard' -w $main | Out-Null
    $restored = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    Assert-Editor $restored 'Editing still works after ScrunchFX.'
    $checks.Add('Keyboard discard is recoverable with the exact text')
    Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory '06-after.png') | Out-Null
    @{ passed=$true; checks=$checks; computerUseVerified=$false; note='WinApp UI Automation only; inspect the screenshots separately.' } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'ui-verification.json')
} catch {
    @{ passed=$false; checks=$checks; error=$_.ToString(); computerUseVerified=$false } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'ui-verification.json')
    throw
}
