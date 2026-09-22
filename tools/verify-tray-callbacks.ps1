param([Parameter(Mandatory)][string]$Executable,
      [Parameter(Mandatory)][string]$DataDirectory,
      [Parameter(Mandatory)][string]$EvidenceDirectory)
# Real Explorer registration, native menus and production commands, driven through
# the documented version-4 notification callback. This is not a mouse-input test.
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/native/Verify.psm1" -Force
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit Scrunch first.' }
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
$oldData=$env:SCRUNCH_DATA_DIRECTORY
$checks=[Collections.Generic.List[string]]::new();$app=$null;$errorText=$null
function Windows {@(Get-ScrunchWindows $app.Id)}
function Notes {@(Windows|Where-Object title -eq 'Scrunch note')}
function Menu {
    [ScrunchNative]::PostMessageW([IntPtr]$main,0x805b,[IntPtr]::Zero,[IntPtr]0x1007b)|Out-Null
    for($i=0;$i -lt 20;$i++) {Start-Sleep -Milliseconds 100;$menu=(Windows|Where-Object className -eq '#32768'|Select-Object -First 1).hwnd;if($menu){return $menu}}
    throw 'Production native tray menu did not appear.'
}
try {
    $started=Start-ScrunchIsolated -Executable $Executable -DataDirectory $DataDirectory
    $app=$started.Process;$main=$started.MainWindow
    $explorer=Get-Process explorer|Select-Object -First 1
    $explorerWindows=Get-ScrunchWindows $explorer.Id
    $taskbar=($explorerWindows|Where-Object className -eq 'Shell_TrayWnd').hwnd
    $overflow=($explorerWindows|Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    if (!$overflow -or ![ScrunchNative]::IsWindowVisible([IntPtr]$overflow)) {
        Invoke-Ui invoke 'Notification Chevron' -w $taskbar|Out-Null;Start-Sleep -Milliseconds 400
    }
    $overflow=(Get-ScrunchWindows $explorer.Id|Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    $trayTree=((Invoke-Ui inspect -w $taskbar)-join "`n")+((Invoke-Ui inspect -w $overflow)-join "`n")
    Assert-Check ($trayTree -match 'Button "Scrunch"') 'Explorer exposes the final payload notification icon by name' $checks
    Assert-Check (Test-TrayIconRegistered -Executable $Executable) 'Final payload registers a real Explorer notification icon' $checks
    $count=(Notes).Count
    $menu=Menu; Invoke-Ui inspect -w $menu | Set-Content (Join-Path $EvidenceDirectory 'native-menu.txt')
    Invoke-Ui send-keys n -w $main --via post-message | Out-Null;Start-Sleep -Milliseconds 400
    Assert-Check ((Notes).Count -eq $count+1) 'Real native tray New note creates a note' $checks
    $note=(Notes|Select-Object -First 1).hwnd
    [ScrunchNative]::PostMessageW([IntPtr]$note,0x10,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null;Start-Sleep -Milliseconds 300
    $menu=Menu;Invoke-Ui send-keys u -w $main --via post-message|Out-Null;Start-Sleep -Milliseconds 400
    Assert-Check ((Notes).Count -eq $count+1) 'Real native tray Undo restores a recoverably discarded note' $checks
    $menu=Menu;Invoke-Ui send-keys s -w $main --via post-message|Out-Null
    Assert-Check ((Notes).Count -eq $count+1) 'Real native tray Show notes preserves every active note' $checks
    $menu=Menu;Invoke-Ui send-keys e -w $main --via post-message|Out-Null;Start-Sleep -Milliseconds 200
    Assert-Check (((Invoke-Ui inspect -w $main) -join "`n") -match 'Back to notes') 'Real native tray Settings opens the existing shell' $checks
    $menu=Menu;Invoke-Ui send-keys q -w $main --via post-message|Out-Null
    Assert-Check ($app.WaitForExit(6000)) 'Real native tray Quit saves and exits' $checks
    Assert-Check (!(Test-TrayIconRegistered -Executable $Executable)) 'Quit removes the real Explorer icon' $checks
    $started=Start-ScrunchIsolated -Executable $Executable -DataDirectory $DataDirectory -ArgumentList '--startup' -Quiet
    $app=$started.Process
    Assert-Check ((Notes).Count -eq $count+1 -and !(Windows|Where-Object title -eq 'Scrunch')) 'Quiet final payload restores notes without showing shell' $checks
    if (![ScrunchNative]::IsWindowVisible([IntPtr]$overflow)) {
        Invoke-Ui invoke 'Notification Chevron' -w $taskbar|Out-Null;Start-Sleep -Milliseconds 400
    }
    Assert-Check (Test-TrayIconRegistered -Executable $Executable) 'Quiet startup restores the same notification identity' $checks
    $redirect=Start-Process $Executable -PassThru;Assert-Check ($redirect.WaitForExit(6000)) 'Manual activation redirects to the resident process' $checks
    Start-Sleep -Milliseconds 200;$main=(Windows|Where-Object title -eq 'Scrunch').hwnd
    Invoke-Ui invoke Quit -w $main|Out-Null;Assert-Check ($app.WaitForExit(6000)) 'Final footer Quit exits cleanly' $checks
} catch {$errorText=$_.ToString();throw}
finally {
    Stop-Scrunch -Process $app -Executable $Executable -TimeoutMilliseconds 5000 | Out-Null
    $env:SCRUNCH_DATA_DIRECTORY=$oldData
    Write-Evidence (Join-Path $EvidenceDirectory 'checks.json') @{
        passed=($null -eq $errorText);checks=$checks;error=$errorText
        input='version-4 callback plus posted native menu mnemonic; not physical mouse'
    } -Depth 4
}
