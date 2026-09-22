param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
if (!$OutputDirectory) { $OutputDirectory = Join-Path (Get-RepoRoot) 'artifacts/scrunchfx-ui' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$checks = [System.Collections.Generic.List[string]]::new()
function Windows { return (Get-ScrunchWindows $AppPid) }
$main = (Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if (!$main) { throw 'Launch a fresh DEBUG --fx-lab session. This script refuses everyday notes.' }
if (Windows | Where-Object title -eq 'Scrunch note') { throw 'Use a fresh FX lab with no existing test notes.' }
function Assert-Editor([long]$window, [string]$expected) {
    $actual = (Invoke-Ui get-value NoteText -w $window --json | ConvertFrom-Json).text
    # Native TextBox TextPattern uses CR paragraph delimiters, while the input
    # command uses LF. Compare the content, not the transport's line endings.
    if (($actual -replace "`r`n?", "`n") -ne ($expected -replace "`r`n?", "`n")) { throw 'Native editor value differs' }
}
try {
    Invoke-Ui invoke NewNote -w $main | Out-Null
    $note = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    $text = "SCRUNCH FX / 21 SEPTEMBER`nActual native ink.`n`nRemember the green notebook."
    Invoke-Ui set-value NoteText $text -w $note | Out-Null
    Assert-Editor $note $text
    $checks.Add('Created a real native note; editor value matches identifying text')
    Invoke-Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory '00-lab.png') | Out-Null
    Invoke-Ui screenshot -w $note --capture-screen -o (Join-Path $OutputDirectory '01-note.png') | Out-Null
    Invoke-Ui invoke FxHold -w $main | Out-Null
    Invoke-Ui set-value FxProgress 0 -w $main | Out-Null
    Invoke-Ui invoke FxDelete -w $main | Out-Null
    $overlay = $null
    for ($i=0; $i -lt 30 -and !$overlay; $i++) {
        Start-Sleep -Milliseconds 100
        $overlay = (Windows | Where-Object title -eq 'ScrunchFX paper').hwnd
    }
    if (!$overlay) { throw 'No native D3D overlay appeared' }
    $index = 2
    foreach ($progress in @(0, .3, .6, 1)) {
        Invoke-Ui set-value FxProgress $progress -w $main | Out-Null
        Start-Sleep -Milliseconds 200
        Invoke-Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory 'lab-overview.png') | Out-Null
        Invoke-Ui screenshot -w $overlay --capture-screen -o (Join-Path $OutputDirectory ("{0:00}-pose-{1}.png" -f $index,$progress)) | Out-Null
        $index++
    }
    $checks.Add('Native overlay present; debug slider accepts 0, 0.3, 0.6, 1; screenshots saved for visual inspection')
    Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Editor $note $text
    $checks.Add('Undo during held playback restores the same editable note with its text')
    Invoke-Ui invoke FxHold -w $main | Out-Null
    Invoke-Ui invoke FxSlow -w $main | Out-Null
    Invoke-Ui invoke FxDelete -w $main | Out-Null
    Start-Sleep -Milliseconds 1300
    if (Windows | Where-Object hwnd -eq $note) { throw 'Deleted note window remains' }
    if (Windows | Where-Object title -eq 'ScrunchFX paper') { throw 'FX overlay remains visible after completion' }
    $checks.Add('Normal-speed delete closes the real note and hides the overlay')
    Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
    $restored = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    Invoke-Ui set-value NoteText 'Editing still works after ScrunchFX.' -w $restored | Out-Null
    Assert-Editor $restored 'Editing still works after ScrunchFX.'
    $checks.Add('Completed discard remains recoverable; normal editing works afterward')
    # A focused native TextBox can consume Delete before parent accelerators.
    # Exercise actual key delivery, not a direct call to the discard method.
    Invoke-Ui click NoteText -w $restored | Out-Null
    Invoke-Ui send-keys 'ctrl+shift+delete' -w $restored --via send-input | Out-Null
    Start-Sleep -Milliseconds 150
    if ((Windows | Where-Object hwnd -eq $restored) -or (Windows | Where-Object title -eq 'ScrunchFX paper')) { throw 'Keyboard discard did not close immediately' }
    $checks.Add('Ctrl+Shift+Delete from the focused native editor closes without an FX overlay')
    Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
    $restored = (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd
    Assert-Editor $restored 'Editing still works after ScrunchFX.'
    $checks.Add('Keyboard discard is recoverable with the exact text')
    Invoke-Ui screenshot -w $main --capture-screen -o (Join-Path $OutputDirectory '06-after.png') | Out-Null
    Write-Evidence (Join-Path $OutputDirectory 'ui-verification.json') @{
        passed=$true; checks=$checks; computerUseVerified=$false
        note='WinApp UI Automation only; inspect the screenshots separately.'
    } -Depth 4
} catch {
    Write-Evidence (Join-Path $OutputDirectory 'ui-verification.json') @{
        passed=$false; checks=$checks; error=$_.ToString(); computerUseVerified=$false
    } -Depth 4
    throw
}
