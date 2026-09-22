param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
if (!$OutputDirectory) { $OutputDirectory = Join-Path (Get-RepoRoot) 'artifacts/scrunchfx-polish-ui' }
$cli = Get-WinAppCli
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Windows { return (Get-ScrunchWindows $AppPid) }
function Note { return (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd }
function Text([long]$hwnd) { return (Invoke-Ui get-value NoteText -w $hwnd --json | ConvertFrom-Json).text }
$main = (Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if (!$main -or (Note)) { throw 'Use a fresh isolated --fx-lab, with no existing test notes.' }
$checks = [Collections.Generic.List[string]]::new()
function Wait-Overlay {
    for ($i=0; $i -lt 40; $i++) {
        $hwnd = (Windows | Where-Object title -eq 'ScrunchFX paper').hwnd
        if ($hwnd) { return $hwnd }; Start-Sleep -Milliseconds 100
    }
    throw 'Overlay did not appear'
}
function Undo-And-Assert([string]$expected) {
    Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
    Start-Sleep -Milliseconds 250
    if ((Text (Note)) -ne $expected) { throw 'Undo did not preserve text' }
}
try {
    Invoke-Ui invoke FxSlow -w $main | Out-Null
    foreach ($sample in @('small-yellow','wide-mint','tall-pink','large-blue','empty-lavender','dense-peach')) {
        Invoke-Ui invoke FxSample -w $main | Out-Null
        Start-Sleep -Milliseconds 350
        $note = Note; $expected = Text $note
        Invoke-Ui screenshot -w $note --capture-screen -o "$OutputDirectory/$sample-source.png" | Out-Null
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui set-value FxProgress 0 -w $main | Out-Null
        Invoke-Ui invoke FxDelete -w $main | Out-Null
        $overlay = Wait-Overlay
        foreach ($pose in @(0,.6,1)) {
            Invoke-Ui set-value FxProgress $pose -w $main | Out-Null
            Start-Sleep -Milliseconds 120
            Invoke-Ui screenshot -w $main --capture-screen -o "$OutputDirectory/overview.png" | Out-Null
            Invoke-Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/$sample-$pose.png" | Out-Null
        }
        Undo-And-Assert $expected
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui invoke FxDelete -w $main | Out-Null
        Start-Sleep -Milliseconds 1400
        if ((Note) -or (Windows | Where-Object title -eq 'ScrunchFX paper')) { throw 'Normal deletion did not finish' }
        Undo-And-Assert $expected
        $checks.Add("$sample`: scrub capture, normal deletion and undo preserve native text")
    }
    # Record the same wide note at normal speed and slowed speed. The samples
    # are pinned only in the isolated lab so the source remains visible on video.
    Invoke-Ui invoke FxSample -w $main | Out-Null
    Invoke-Ui invoke FxSample -w $main | Out-Null
    $expected = Text (Note)
    foreach ($mode in @('normal','slow')) {
        $duration = if ($mode -eq 'normal') { 4 } else { 21 }
        if ($mode -eq 'slow') { Invoke-Ui invoke FxSlow -w $main | Out-Null }
        $recording = Start-Process -FilePath $cli -WindowStyle Hidden -PassThru -ArgumentList @(
            'ui','record','-w',"$main",'--duration-sec',"$duration",'--fps','30','--frames',
            '--capture-screen','--max-edge','1240','-o',"$OutputDirectory/$mode.mp4")
        Start-Sleep -Milliseconds 1200
        Invoke-Ui invoke FxDelete -w $main | Out-Null
        if (!$recording.WaitForExit(($duration+10)*1000) -or $recording.ExitCode -ne 0) { throw 'Recording failed' }
        if ((Note) -or (Windows | Where-Object title -eq 'ScrunchFX paper')) { throw 'Recorded deletion did not finish' }
        Undo-And-Assert $expected
        $checks.Add("$mode playback recorded and completed; undo preserved text")
    }
    Invoke-Ui invoke FxSlow -w $main | Out-Null
    # Maximize the neutral lab backdrop for corner captures; note placement uses
    # native work-area coordinates, including shadow padding outside the screen.
    Invoke-Ui invoke Maximize-Restore -w $main | Out-Null
    for ($corner=0; $corner -lt 4; $corner++) {
        Invoke-Ui invoke FxCorner -w $main | Out-Null
        Start-Sleep -Milliseconds 200
        Invoke-Ui screenshot -w (Note) --capture-screen -o "$OutputDirectory/corner-$corner-source.png" | Out-Null
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui set-value FxProgress .6 -w $main | Out-Null
        Invoke-Ui invoke FxDelete -w $main | Out-Null
        $overlay = Wait-Overlay
        Invoke-Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/corner-$corner-fold.png" | Out-Null
        Undo-And-Assert $expected
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui invoke FxDelete -w $main | Out-Null
        Start-Sleep -Milliseconds 1400
        if (Note) { throw 'Corner deletion did not finish' }
        Undo-And-Assert $expected
        $checks.Add("Corner $corner`: held fold, normal deletion and undo")
    }
    Invoke-Ui set-value NoteText 'Polish verification complete. Editing still works.' -w (Note) | Out-Null
    if ((Text (Note)) -ne 'Polish verification complete. Editing still works.') { throw 'Editing failed' }
    $checks.Add('Native editing works after the full sample and corner sequence')
    Write-Evidence "$OutputDirectory/polish-ui-verification.json" @{
        passed=$true; checks=$checks
        method='WinApp UI Automation, native screenshots and recording; inspect images separately'
    } -Depth 4
} catch {
    Write-Evidence "$OutputDirectory/polish-ui-verification.json" @{passed=$false; checks=$checks; error=$_.ToString()} -Depth 4
    throw
}
