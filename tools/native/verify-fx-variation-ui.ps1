param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory,
      [switch]$ProductionOnly)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
$root = Get-RepoRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/scrunchfx-variation-ui' }
$cli = Get-WinAppCli
$seeds = (Get-Content (Join-Path $root 'tests/fixtures/fx-golden-seeds.json') | ConvertFrom-Json).seeds
# Supplement the existing all-seeds square sweep and all-families aspect sweep.
# Reflections now run on different rectangles; timing extremes include dense ink.
$sampleIndices = @(3,3,3,1,2,0,2,0,1,0,1,2,5,4,5,3,4,5)
$shapes = @(@(220,180),@(440,180),@(220,440),@(440,440),@(300,320),@(300,320))
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Windows { return (Get-ScrunchWindows $AppPid) }
function Note { return (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd }
$main = (Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if (!$main -or (Note)) { throw 'Use a fresh isolated lab with no test notes.' }
function Wait-Overlay {
    for ($i=0; $i -lt 50; $i++) {
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
$rows = [Collections.Generic.List[object]]::new()
try {
    Invoke-Ui invoke Maximize-Restore -w $main | Out-Null
    Invoke-Ui invoke FxProduction -w $main | Out-Null
    $currentSample = -1
    for ($index=0; $index -lt $seeds.Count; $index++) {
        $seed = $seeds[$index]; $targetSample = $sampleIndices[$index]
        while ($currentSample -ne $targetSample) {
            Invoke-Ui invoke FxSample -w $main | Out-Null
            $currentSample = ($currentSample + 1) % 6
            Start-Sleep -Milliseconds 180
        }
        Invoke-Ui set-value FxSeed $seed -w $main | Out-Null
        $expected = (Invoke-Ui get-value NoteText -w (Note) --json | ConvertFrom-Json).text
        Invoke-Ui screenshot -w (Note) --capture-screen -o "$OutputDirectory/seed-$seed-source.png" | Out-Null
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Invoke-Ui set-value FxProgress 0 -w $main | Out-Null
        Invoke-Ui invoke FxReplay -w $main | Out-Null
        $overlay = Wait-Overlay
        foreach ($pose in @(0,.4,.7,1)) {
            Invoke-Ui set-value FxProgress $pose -w $main | Out-Null
            Start-Sleep -Milliseconds 120
            Invoke-Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/seed-$seed-pose-$pose.png" | Out-Null
        }
        Invoke-Ui invoke FxHold -w $main | Out-Null
        Wait-Idle
        $modes = if ($ProductionOnly) { @('production') } else { @('production','slow') }
        foreach ($mode in $modes) {
            $duration = if ($mode -eq 'production') { 3 } else { 21 }
            if ($mode -eq 'slow') { Invoke-Ui invoke FxSlow -w $main | Out-Null }
            $recording = Start-Process -FilePath $cli -WindowStyle Hidden -PassThru -ArgumentList @(
                'ui','record','-w',"$main",'--duration-sec',"$duration",'--fps','30','--frames',
                '--capture-screen','--max-edge','1600','-o',"$OutputDirectory/seed-$seed-$mode.mp4")
            Start-Sleep -Milliseconds 850
            Invoke-Ui invoke FxDelete -w $main | Out-Null
            if (!$recording.WaitForExit(($duration+10)*1000) -or $recording.ExitCode -ne 0) { throw 'Recording failed' }
            Wait-Idle
            if (Note) { throw "Seed $seed $mode did not close" }
            Invoke-Ui invoke 'Undo last discard' -w $main | Out-Null
            Start-Sleep -Milliseconds 300
            if (!(Note) -or (Invoke-Ui get-value NoteText -w (Note) --json | ConvertFrom-Json).text -ne $expected) { throw 'Undo failed to restore exact text' }
            if ($mode -eq 'slow') { Invoke-Ui invoke FxProduction -w $main | Out-Null }
        }
        $rows.Add([pscustomobject]@{seed=$seed;sample=$targetSample;width=$shapes[$targetSample][0];height=$shapes[$targetSample][1];outcome="$($modes -join ' and ') saved discard closed; exact-text Undo; dormant";modes=$modes;held=@(0,.4,.7,1)})
        Write-Evidence "$OutputDirectory/variation-ui-verification.json" @{passed=$false;inProgress=$true;rows=$rows}
        Write-Host "Captured matrix seed $seed ($($shapes[$targetSample] -join 'x')): $($modes -join ', '), held"
    }
    Write-Evidence "$OutputDirectory/variation-ui-verification.json" @{
        passed=$true; rows=$rows; method='Native WinApp recordings and held captures; visual assessment separate'
    }
} catch {
    Write-Evidence "$OutputDirectory/variation-ui-verification.json" @{passed=$false;rows=$rows;error=$_.ToString()}
    throw
}
