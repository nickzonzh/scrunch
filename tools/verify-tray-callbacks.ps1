param([Parameter(Mandatory)][string]$Executable,
      [Parameter(Mandatory)][string]$DataDirectory,
      [Parameter(Mandatory)][string]$EvidenceDirectory)
# Real Explorer registration, native menus and production commands, driven through
# the documented version-4 notification callback. This is not a mouse-input test.
$ErrorActionPreference='Stop'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit Scrunch first.' }
$cli=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
$oldData=$env:SCRUNCH_DATA_DIRECTORY; $env:SCRUNCH_DATA_DIRECTORY=[IO.Path]::GetFullPath($DataDirectory)
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class TrayRelease {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct IconId {public uint Size;public IntPtr Window;public uint Id;public Guid Guid;}
 [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref IconId id,out Rect rect);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr w,uint m,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
}
'@
$checks=[Collections.Generic.List[string]]::new();$app=$null;$errorText=$null
function Check([bool]$ok,[string]$text){if(!$ok){throw $text};$checks.Add($text);Write-Host "PASS: $text"}
function Ui {$r=& $cli ui @args;if($LASTEXITCODE){throw "UI failed: $args"};$r}
function Windows {@(Ui list-windows -a $app.Id --json|ConvertFrom-Json)}
function Notes {@(Windows|Where-Object title -eq 'Scrunch note')}
function Menu {
    [TrayRelease]::PostMessageW([IntPtr]$main,0x805b,[IntPtr]::Zero,[IntPtr]0x1007b)|Out-Null
    for($i=0;$i -lt 20;$i++) {Start-Sleep -Milliseconds 100;$menu=(Windows|Where-Object className -eq '#32768'|Select-Object -First 1).hwnd;if($menu){return $menu}}
    throw 'Production native tray menu did not appear.'
}
$hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('Scrunch.NotificationIcon.v1|'+[IO.Path]::GetFullPath($Executable).ToUpperInvariant()))
$icon=[TrayRelease+IconId]::new();$icon.Size=[Runtime.InteropServices.Marshal]::SizeOf($icon);$icon.Guid=[guid]::new([byte[]]$hash[0..15]);$rect=[TrayRelease+Rect]::new()
try {
    $app=Start-Process $Executable -PassThru;Start-Sleep -Milliseconds 1600
    $main=(Windows|Where-Object title -eq 'Scrunch').hwnd
    $explorer=Get-Process explorer|Select-Object -First 1
    $explorerWindows=@(Ui list-windows -a $explorer.Id --json|ConvertFrom-Json)
    $taskbar=($explorerWindows|Where-Object className -eq 'Shell_TrayWnd').hwnd
    $overflow=($explorerWindows|Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    if (!$overflow -or ![TrayRelease]::IsWindowVisible([IntPtr]$overflow)) {
        Ui invoke 'Notification Chevron' -w $taskbar|Out-Null;Start-Sleep -Milliseconds 400
    }
    $overflow=(Ui list-windows -a $explorer.Id --json|ConvertFrom-Json|Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    $trayTree=((Ui inspect -w $taskbar)-join "`n")+((Ui inspect -w $overflow)-join "`n")
    Check ($trayTree -match 'Button "Scrunch"') 'Explorer exposes the final payload notification icon by name'
    Check ([TrayRelease]::Shell_NotifyIconGetRect([ref]$icon,[ref]$rect) -eq 0) 'Final payload registers a real Explorer notification icon'
    $count=(Notes).Count
    $menu=Menu; Ui inspect -w $menu | Set-Content (Join-Path $EvidenceDirectory 'native-menu.txt')
    Ui send-keys n -w $main --via post-message | Out-Null;Start-Sleep -Milliseconds 400
    Check ((Notes).Count -eq $count+1) 'Real native tray New note creates a note'
    $note=(Notes|Select-Object -First 1).hwnd
    [TrayRelease]::PostMessageW([IntPtr]$note,0x10,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null;Start-Sleep -Milliseconds 300
    $menu=Menu;Ui send-keys u -w $main --via post-message|Out-Null;Start-Sleep -Milliseconds 400
    Check ((Notes).Count -eq $count+1) 'Real native tray Undo restores a recoverably discarded note'
    $menu=Menu;Ui send-keys s -w $main --via post-message|Out-Null
    Check ((Notes).Count -eq $count+1) 'Real native tray Show notes preserves every active note'
    $menu=Menu;Ui send-keys e -w $main --via post-message|Out-Null;Start-Sleep -Milliseconds 200
    Check (((Ui inspect -w $main) -join "`n") -match 'Back to notes') 'Real native tray Settings opens the existing shell'
    $menu=Menu;Ui send-keys q -w $main --via post-message|Out-Null
    Check ($app.WaitForExit(6000)) 'Real native tray Quit saves and exits'
    Check ([TrayRelease]::Shell_NotifyIconGetRect([ref]$icon,[ref]$rect) -ne 0) 'Quit removes the real Explorer icon'
    $app=Start-Process $Executable -ArgumentList '--startup' -PassThru;Start-Sleep -Milliseconds 1600
    Check ((Notes).Count -eq $count+1 -and !(Windows|Where-Object title -eq 'Scrunch')) 'Quiet final payload restores notes without showing shell'
    if (![TrayRelease]::IsWindowVisible([IntPtr]$overflow)) {
        Ui invoke 'Notification Chevron' -w $taskbar|Out-Null;Start-Sleep -Milliseconds 400
    }
    Check ([TrayRelease]::Shell_NotifyIconGetRect([ref]$icon,[ref]$rect) -eq 0) 'Quiet startup restores the same notification identity'
    $redirect=Start-Process $Executable -PassThru;Check ($redirect.WaitForExit(6000)) 'Manual activation redirects to the resident process'
    Start-Sleep -Milliseconds 200;$main=(Windows|Where-Object title -eq 'Scrunch').hwnd
    Ui invoke Quit -w $main|Out-Null;Check ($app.WaitForExit(6000)) 'Final footer Quit exits cleanly'
} catch {$errorText=$_.ToString();throw}
finally {
    if($app -and !$app.HasExited){
        $redirect=Start-Process $Executable -PassThru;$redirect.WaitForExit(5000)|Out-Null
        $main=(Windows|Where-Object title -eq 'Scrunch').hwnd
        if($main){if(((Ui inspect -w $main)-join "`n") -match 'Back to notes'){Ui invoke BackToNotes -w $main|Out-Null};Ui invoke Quit -w $main|Out-Null}
    }
    $env:SCRUNCH_DATA_DIRECTORY=$oldData
    @{passed=($null -eq $errorText);checks=$checks;error=$errorText;input='version-4 callback plus posted native menu mnemonic; not physical mouse'}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $EvidenceDirectory 'checks.json')
}
