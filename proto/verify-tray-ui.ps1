param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'artifacts/scrunch-compact'),
      [string]$DataDirectory,
      [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$cli = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
$exe = Join-Path $BuildDirectory 'Scrunch.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit running Scrunch builds before native tray verification.' }
$evidence = if ($EvidenceDirectory) { $EvidenceDirectory } else { Join-Path $BuildDirectory 'tray-desktop' }
$originalDataDirectory = $env:SCRUNCH_DATA_DIRECTORY
if ($DataDirectory) { $env:SCRUNCH_DATA_DIRECTORY = [IO.Path]::GetFullPath($DataDirectory) }
New-Item -ItemType Directory -Force $evidence | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TrayDesktopCheck {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct IconId {public uint Size;public IntPtr Window;public uint Id;public Guid Guid;}
 [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref IconId id,out Rect rect);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
}
'@
function Ui { $result = & $cli ui @args; if($LASTEXITCODE -ne 0){throw "Native UI failed: $args : $result"}; return $result }
function Windows([int]$processId) { return @(Ui list-windows -a $processId --json | ConvertFrom-Json) }
function Capture([long]$handle,[string]$name) {
    $r=[TrayDesktopCheck+Rect]::new()
    [TrayDesktopCheck]::GetWindowRect([IntPtr]$handle,[ref]$r) | Out-Null
    $bitmap=[Drawing.Bitmap]::new($r.Right-$r.Left,$r.Bottom-$r.Top)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($r.Left,$r.Top,0,0,$bitmap.Size); $bitmap.Save((Join-Path $evidence "$name.png")) }
    finally { $graphics.Dispose();$bitmap.Dispose() }
}
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$label) { if(!$condition){throw $label};$checks.Add($label);Write-Host "PASS: $label" }
$explorer = Get-Process explorer | Select-Object -First 1
$taskbar = (Windows $explorer.Id | Where-Object className -eq 'Shell_TrayWnd').hwnd
if(!$taskbar){throw 'Run this verification on the real interactive Windows desktop.'}
function TraySurface {
    $tree = (Ui inspect -w $taskbar) -join "`n"
    if ($tree -match 'Button "Scrunch"') { [TrayDesktopCheck]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null; return $taskbar }
    $overflow = (Windows $explorer.Id | Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    if ($overflow -and [TrayDesktopCheck]::IsWindowVisible([IntPtr]$overflow)) {
        $tree = (Ui inspect -w $overflow) -join "`n"
        if ($tree -match 'Button "Scrunch"') { [TrayDesktopCheck]::SetForegroundWindow([IntPtr]$overflow) | Out-Null; return $overflow }
    }
    [TrayDesktopCheck]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null
    Ui invoke 'Notification Chevron' -w $taskbar | Out-Null
    Start-Sleep -Milliseconds 300
    $overflow = (Windows $explorer.Id | Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    $tree = (Ui inspect -w $overflow) -join "`n"
    if ($tree -notmatch 'Button "Scrunch"') { throw 'Explorer did not expose the Scrunch icon' }
    $tree | Set-Content (Join-Path $evidence 'tray-uia.txt')
    [TrayDesktopCheck]::SetForegroundWindow([IntPtr]$overflow) | Out-Null
    return $overflow
}
function Menu {
    $surface=TraySurface
    Ui click Scrunch -w $surface --right | Out-Null
    $menu = $null
    for ($attempt = 0; $attempt -lt 15 -and !$menu; $attempt++) {
        Start-Sleep -Milliseconds 100
        $menu=(Windows $app.Id | Where-Object { $_.className -eq '#32768' -and [TrayDesktopCheck]::IsWindowVisible([IntPtr]$_.hwnd) }).hwnd
    }
    if(!$menu){throw 'Native shortcut menu did not appear'}
    return $menu
}
$app = $null
try {
    $notebook = if ($DataDirectory) { Join-Path $DataDirectory 'notes.json' } else { Join-Path $BuildDirectory 'product-check-tray/notes.json' }
    $saved = Get-Content $notebook -Raw | ConvertFrom-Json
    $expectedNotes = @($saved.Notes | Where-Object { !$_.DeletedAt }).Count
    $app = Start-Process $exe -ArgumentList '--tray-check' -PassThru
    Start-Sleep -Milliseconds 1500
    $main=(Windows $app.Id | Where-Object title -eq 'Scrunch').hwnd
    Check ([bool]$main) 'Cold launch exposes one native shell'
    $shellRect=[TrayDesktopCheck+Rect]::new()
    [TrayDesktopCheck]::GetWindowRect([IntPtr]$main,[ref]$shellRect) | Out-Null
    # Avoid leaving the cursor over taskbar hover flyouts from another test run.
    [TrayDesktopCheck]::SetCursorPos($shellRect.Left+40,$shellRect.Top+15) | Out-Null
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes) 'Saved active notes return after a clean quit'
    $surface=TraySurface
    Capture $surface 'tray-actual-size'
    Check (((Ui inspect -w $surface) -join "`n") -match 'Button "Scrunch"') 'Explorer exposes the Scrunch tooltip/accessibility name'
    Ui click Scrunch -w $surface | Out-Null
    Start-Sleep -Milliseconds 250
    Check ([TrayDesktopCheck]::IsWindowVisible([IntPtr]$main)) 'Physical tray left-click shows the existing shell'
    Capture $main 'tray-shell'
    [TrayDesktopCheck]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null
    Ui invoke 'Notification Chevron' -w $taskbar | Out-Null
    Start-Sleep -Milliseconds 200
    Check (![TrayDesktopCheck]::IsWindowVisible([IntPtr]$main)) 'Physical click away dismisses the tray-opened shell'
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes) 'Click-away keeps notes visible'
    $menu=Menu
    $tree=(Ui inspect -w $menu) -join "`n"
    $tree | Set-Content (Join-Path $evidence 'menu-uia.txt')
    Capture $menu 'native-menu'
    Check ($tree -match 'New note' -and $tree -match 'Show notes' -and $tree -match 'Undo last discard.*disabled' -and $tree -match 'Settings' -and $tree -match 'Quit') 'Native menu contains every expected action and disables unavailable Undo'
    Ui send-keys 'n' -w $main --via send-input | Out-Null
    Start-Sleep -Milliseconds 400
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native menu keyboard New note creates a visible editor'
    $newNote=(Windows $app.Id | Where-Object { $_.title -eq 'Scrunch note' -and $_.isForeground }).hwnd
    if(!$newNote){throw 'New note did not focus its native editor'}
    [TrayDesktopCheck]::SendMessageW([IntPtr]$newNote,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    $menu=Menu
    $undoTree=(Ui inspect -w $menu) -join "`n"
    Check ($undoTree -match 'Undo last discard' -and $undoTree -notmatch 'Undo last discard.*disabled') 'Native Undo becomes enabled after a recoverable discard'
    Ui invoke 'Undo last discard' -w $menu | Out-Null
    Start-Sleep -Milliseconds 300
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native tray Undo restores the discarded physical note'
    $menu=Menu; Ui invoke 'Show notes' -w $menu | Out-Null
    Start-Sleep -Milliseconds 150
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native Show notes leaves all notes visible'
    $menu=Menu; Ui invoke Settings -w $menu | Out-Null
    Start-Sleep -Milliseconds 250
    Check (((Ui inspect -w $main) -join "`n") -match 'Back to notes') 'Native tray Settings opens the existing settings view'
    Ui invoke BackToNotes -w $main | Out-Null
    [TrayDesktopCheck]::SendMessageW([IntPtr]$main,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Check (!$app.HasExited -and ![TrayDesktopCheck]::IsWindowVisible([IntPtr]$main)) 'Native shell close hides without terminating the process'
    # Global shortcut delivered with Explorer, a different process, foreground.
    $before=@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count
    Ui send-keys 'ctrl+alt+n' -w $taskbar --via send-input | Out-Null
    Start-Sleep -Milliseconds 500
    Check (@(Windows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $before+1) 'Global shortcut works with another application foreground and shell hidden'
    $menu=Menu; Ui invoke Quit -w $menu | Out-Null
    Check ($app.WaitForExit(5000)) 'Native tray Quit terminates the resident process'
    $app=$null
    $hash=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('Scrunch.NotificationIcon.v1|'+[IO.Path]::GetFullPath($exe).ToUpperInvariant()))
    $iconId=[TrayDesktopCheck+IconId]::new()
    $iconId.Size=[Runtime.InteropServices.Marshal]::SizeOf($iconId);$iconId.Guid=[guid]::new([byte[]]$hash[0..15])
    $rect=[TrayDesktopCheck+Rect]::new()
    Check ([TrayDesktopCheck]::Shell_NotifyIconGetRect([ref]$iconId,[ref]$rect) -ne 0) 'Explicit Quit removes the real notification icon'
    $app=Start-Process $exe -ArgumentList '--tray-check','--startup' -PassThru
    Start-Sleep -Milliseconds 1500
    $restored=Windows $app.Id
    Check (@($restored | Where-Object title -eq 'Scrunch').Count -eq 0 -and @($restored | Where-Object title -eq 'Scrunch note').Count -eq $before+1) 'Quiet startup restores all notes while leaving shell hidden'
    $redirect=Start-Process $exe -ArgumentList '--tray-check' -PassThru
    Check ($redirect.WaitForExit(5000)) 'Manual launch redirects to the quiet resident process'
    Start-Sleep -Milliseconds 250
    $main=(Windows $app.Id | Where-Object title -eq 'Scrunch').hwnd
    Check ([bool]$main) 'Manual reactivation reveals the quiet shell'
    Ui invoke Quit -w $main | Out-Null
    Check ($app.WaitForExit(5000)) 'Footer Quit also exits cleanly after restart'
    $app=$null
} finally {
    $checks | ConvertTo-Json | Set-Content (Join-Path $evidence 'checks.json')
    if($app -and !$app.HasExited){
        $redirect=Start-Process $exe -ArgumentList '--tray-check' -PassThru
        $redirect.WaitForExit(5000) | Out-Null
        $main=(Windows $app.Id | Where-Object title -eq 'Scrunch').hwnd
        if($main){Ui invoke Quit -w $main | Out-Null}
    }
    $env:SCRUNCH_DATA_DIRECTORY = $originalDataDirectory
}
Write-Host "Desktop evidence: $evidence"
