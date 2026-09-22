param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory,
      [uint[]]$Seeds,
      [switch]$Record, [switch]$Aspects)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
$root = Get-RepoRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/scrunchfx-signature-ui' }
if (!$Seeds) { $Seeds = (Get-Content (Join-Path $root 'tests/fixtures/fx-golden-seeds.json') | ConvertFrom-Json).seeds }
$cli = Get-WinAppCli
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Windows { return (Get-ScrunchWindows $AppPid) }
function Note { return (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd }
$main = (Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if (!$main) { throw 'Expected an isolated FX lab' }
function Wait-Overlay {
    for ($i=0; $i -lt 40; $i++) {
        $hwnd = (Windows | Where-Object title -eq 'ScrunchFX paper').hwnd
        if ($hwnd) { return $hwnd }; Start-Sleep -Milliseconds 100
    }
    throw 'Overlay did not appear'
}
function Wait-Idle {
    for ($i=0; $i -lt 50; $i++) {
        if (!(Windows | Where-Object title -eq 'ScrunchFX paper')) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'Overlay failed to become dormant'
}
$checks = [Collections.Generic.List[string]]::new()
try {
    # Use a fresh lab: defaults are slow on, hold off. Maximize provides a
    # neutral desktop backdrop; every capture remains an actual native frame.
    if (Windows | Where-Object title -eq 'ScrunchFX paper') { throw 'Start with an idle, fresh lab' }
    Invoke-Ui invoke Maximize-Restore -w $main | Out-Null
    Invoke-Ui invoke FxSlow -w $main | Out-Null
    if (!(Note)) { Invoke-Ui invoke FxSample -w $main | Out-Null; Start-Sleep -Milliseconds 300 }
    # Native square sample with readable test text, not a generated texture.
    Invoke-Ui invoke FxSample -w $main | Out-Null
    Invoke-Ui invoke FxSample -w $main | Out-Null
    Invoke-Ui invoke FxSample -w $main | Out-Null
    Start-Sleep -Milliseconds 300
    foreach ($seed in $Seeds) {
        Invoke-Ui set-value FxSeed $seed -w $main | Out-Null
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui set-value FxProgress 0 -w $main | Out-Null
        Invoke-Ui invoke FxReplay -w $main | Out-Null
        $overlay = Wait-Overlay
        foreach ($pose in @(0,.2,.4,.6,.8,1)) {
            Invoke-Ui set-value FxProgress $pose -w $main | Out-Null
            Start-Sleep -Milliseconds 90
            Invoke-Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/seed-$seed-pose-$pose.png" | Out-Null
        }
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Wait-Idle
        $checks.Add("Seed $seed captured at six deformation stages; replay finished dormant")
    }
    if ($Aspects) {
        # Four further calls bring sample 3 back to sample 1; instead cycle all
        # six existing samples, covering min/wide/tall/max plus empty/dense ink.
        for ($sample=0; $sample -lt 6; $sample++) {
            Invoke-Ui invoke FxSample -w $main | Out-Null
            Start-Sleep -Milliseconds 200
            Invoke-Ui screenshot -w (Note) --capture-screen -o "$OutputDirectory/aspect-$sample-source.png" | Out-Null
            foreach ($seed in @(0,1,2)) {
                Invoke-Ui set-value FxSeed $seed -w $main | Out-Null
                Invoke-Ui invoke FxHold -w $main | Out-Null
                Invoke-Ui invoke FxReplay -w $main | Out-Null
                $overlay = Wait-Overlay
                foreach ($pose in @(.4,.7,1)) {
                    Invoke-Ui set-value FxProgress $pose -w $main | Out-Null
                    Start-Sleep -Milliseconds 90
                    Invoke-Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/aspect-$sample-seed-$seed-pose-$pose.png" | Out-Null
                }
                Invoke-Ui invoke FxHold -w $main | Out-Null
                Wait-Idle
            }
            $checks.Add("Aspect sample $sample captured for all primary families")
        }
    }
    if ($Record) {
        foreach ($seed in $Seeds) {
            Invoke-Ui set-value FxSeed $seed -w $main | Out-Null
            $recording = Start-Process -FilePath $cli -WindowStyle Hidden -PassThru -ArgumentList @(
                'ui','record','-w',"$main",'--duration-sec','3','--fps','30','--frames',
                '--capture-screen','--max-edge','1600','-o',"$OutputDirectory/seed-$seed-production.mp4")
            Start-Sleep -Milliseconds 850
            Invoke-Ui invoke FxDelete -w $main | Out-Null
            if (!$recording.WaitForExit(12000) -or $recording.ExitCode -ne 0) { throw 'Recording failed' }
            Wait-Idle
            if (Note) { throw 'Recorded product discard did not close the note' }
            Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
            Start-Sleep -Milliseconds 300
            if (!(Note)) { throw 'Undo failed after recorded discard' }
            $checks.Add("Seed $seed production discard recorded, closed and restored through Undo")
        }
    }
    Write-Evidence "$OutputDirectory/seed-ui-verification.json" @{
        passed=$true; checks=$checks; computerUseVerified=$false
        method='WinApp native UIA, screen captures and recordings; visual judgement recorded separately'
    } -Depth 4
} catch {
    Write-Evidence "$OutputDirectory/seed-ui-verification.json" @{passed=$false;checks=$checks;error=$_.ToString()} -Depth 4
    throw
}
