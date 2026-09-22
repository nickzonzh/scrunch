param([Parameter(Mandatory)][int]$AppPid,
      [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/scrunchfx-variation-moderate-ui'),
      [switch]$Resume)
$ErrorActionPreference='Stop'
$cli=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
function Ui { $r=& $cli ui @args; if($LASTEXITCODE){throw "WinApp failed: $args`n$r"}; return $r }
function Windows { Ui list-windows -a $AppPid --json | ConvertFrom-Json }
function Note { (Windows | Where-Object title -eq 'Scrunch note' | Select-Object -Last 1).hwnd }
$main=(Windows | Where-Object title -eq 'Scrunch — isolated FX lab').hwnd
if(!$main -or !(Note)){throw 'Use an idle isolated lab with a sample note on the 100% scale QA display.'}
function Editor { (Ui inspect NoteText -w (Note) --json | ConvertFrom-Json).windows[0].elements[0] }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$rows=[Collections.Generic.List[object]]::new()
if($Resume){foreach($row in (Get-Content "$OutputDirectory/moderate-ui-verification.json"|ConvertFrom-Json).rows){$rows.Add($row)}}
try {
    Ui invoke FxProduction -w $main | Out-Null
    foreach($shape in @(@(360,240),@(260,360))) {
        if(@($rows | Where-Object { $_.width -eq $shape[0] -and $_.height -eq $shape[1] }).Count -eq 3){continue}
        for($attempt=0;$attempt -lt 20;$attempt++) {
            $editor=Editor
            if($editor.width -eq $shape[0] -and $editor.height+30 -eq $shape[1]){break}
            Start-Sleep -Milliseconds 100
        }
        # The existing native resize grip occupies the bottom-right 24 DIPs.
        # NoteText ends at the paper bottom and starts below its 30-DIP header.
        $x=$editor.x+$editor.width-12; $y=$editor.y+$editor.height-12
        $tx=$x+$shape[0]-$editor.width; $ty=$y+$shape[1]-($editor.height+30)
        Ui drag "$x,$y" "$tx,$ty" -w (Note) | Out-Null
        $editor=Editor
        if($editor.width -ne $shape[0] -or $editor.height+30 -ne $shape[1]){throw 'Resize did not reach the requested paper dimensions; check display scaling.'}
        foreach($seed in @(0,4,11)) {
            $prefix="$($shape[0])x$($shape[1])-seed-$seed"
            if($rows | Where-Object prefix -eq $prefix){continue}
            Ui set-value FxSeed $seed -w $main | Out-Null
            $text=(Ui get-value NoteText -w (Note) --json | ConvertFrom-Json).text
            Ui invoke FxHold -w $main | Out-Null
            Ui invoke FxReplay -w $main | Out-Null
            Start-Sleep -Milliseconds 300
            $overlay=(Windows | Where-Object title -eq 'ScrunchFX paper').hwnd
            if(!$overlay){throw 'Held overlay absent'}
            foreach($pose in @(.4,.7,1)) {
                Ui set-value FxProgress $pose -w $main | Out-Null
                Start-Sleep -Milliseconds 100
                Ui screenshot -w $overlay --capture-screen -o "$OutputDirectory/$prefix-pose-$pose.png" | Out-Null
            }
            Ui invoke FxHold -w $main | Out-Null
            Start-Sleep -Milliseconds 400
            foreach($mode in @('production','slow')) {
                if($mode -eq 'slow'){Ui invoke FxSlow -w $main | Out-Null}
                $duration=if($mode -eq 'slow'){21}else{3}
                $recording=Start-Process -FilePath $cli -WindowStyle Hidden -PassThru -ArgumentList @(
                    'ui','record','-w',"$main",'--duration-sec',"$duration",'--fps','30','--frames',
                    '--capture-screen','--max-edge','1600','-o',"$OutputDirectory/$prefix-$mode.mp4")
                Start-Sleep -Milliseconds 850
                Ui invoke FxDelete -w $main | Out-Null
                if(!$recording.WaitForExit(($duration+10)*1000) -or $recording.ExitCode -ne 0){throw 'Recording failed'}
                if((Note) -or (Windows | Where-Object title -eq 'ScrunchFX paper')){throw 'Discard did not close and become dormant'}
                Ui invoke 'Undo last discard' -w $main | Out-Null
                Start-Sleep -Milliseconds 300
                if((Ui get-value NoteText -w (Note) --json | ConvertFrom-Json).text -ne $text){throw 'Undo changed text'}
                $editor=Editor
                if($editor.width -ne $shape[0] -or $editor.height+30 -ne $shape[1]){throw 'Undo changed paper dimensions'}
                Ui invoke FxProduction -w $main | Out-Null
            }
            $rows.Add(@{seed=$seed;width=$shape[0];height=$shape[1];prefix=$prefix;outcome='Production/slow closed, dormant; exact text and dimensions restored'})
            @{passed=$false;inProgress=$true;rows=$rows}|ConvertTo-Json -Depth 5|Set-Content "$OutputDirectory/moderate-ui-verification.json"
            Write-Host "Captured $prefix at production/slow speed and three held poses"
        }
    }
    @{passed=$true;rows=$rows}|ConvertTo-Json -Depth 5|Set-Content "$OutputDirectory/moderate-ui-verification.json"
} catch {
    @{passed=$false;rows=$rows;error=$_.ToString()}|ConvertTo-Json -Depth 5|Set-Content "$OutputDirectory/moderate-ui-verification.json"
    throw
}
