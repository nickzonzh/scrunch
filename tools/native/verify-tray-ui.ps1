param([string]$BuildDirectory,
      [string]$DataDirectory,
      [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
if (!$BuildDirectory) { $BuildDirectory = Join-Path (Get-RepoRoot) 'artifacts/debug' }
$exe = Join-Path $BuildDirectory 'Scrunch.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit running Scrunch builds before native tray verification.' }
$evidence = if ($EvidenceDirectory) { $EvidenceDirectory } else { Join-Path $BuildDirectory 'tray-desktop' }
$originalDataDirectory = $env:SCRUNCH_DATA_DIRECTORY
if ($DataDirectory) { $env:SCRUNCH_DATA_DIRECTORY = [IO.Path]::GetFullPath($DataDirectory) }
New-Item -ItemType Directory -Force $evidence | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$explorer = Get-Process explorer | Select-Object -First 1
$taskbar = (Get-ScrunchWindows $explorer.Id | Where-Object className -eq 'Shell_TrayWnd').hwnd
if(!$taskbar){throw 'Run this verification on the real interactive Windows desktop.'}
function TraySurface {
    $tree = (Invoke-Ui inspect -w $taskbar) -join "`n"
    if ($tree -match 'Button "Scrunch"') { [ScrunchNative]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null; return $taskbar }
    $overflow = (Get-ScrunchWindows $explorer.Id | Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    if ($overflow -and [ScrunchNative]::IsWindowVisible([IntPtr]$overflow)) {
        $tree = (Invoke-Ui inspect -w $overflow) -join "`n"
        if ($tree -match 'Button "Scrunch"') { [ScrunchNative]::SetForegroundWindow([IntPtr]$overflow) | Out-Null; return $overflow }
    }
    [ScrunchNative]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null
    Invoke-Ui invoke 'Notification Chevron' -w $taskbar | Out-Null
    Start-Sleep -Milliseconds 300
    $overflow = (Get-ScrunchWindows $explorer.Id | Where-Object className -eq 'NotifyIconOverflowWindow').hwnd
    $tree = (Invoke-Ui inspect -w $overflow) -join "`n"
    if ($tree -notmatch 'Button "Scrunch"') { throw 'Explorer did not expose the Scrunch icon' }
    $tree | Set-Content (Join-Path $evidence 'tray-uia.txt')
    [ScrunchNative]::SetForegroundWindow([IntPtr]$overflow) | Out-Null
    return $overflow
}
function Menu {
    $surface=TraySurface
    Invoke-Ui click Scrunch -w $surface --right | Out-Null
    $menu = $null
    for ($attempt = 0; $attempt -lt 15 -and !$menu; $attempt++) {
        Start-Sleep -Milliseconds 100
        $menu=(Get-ScrunchWindows $app.Id | Where-Object { $_.className -eq '#32768' -and [ScrunchNative]::IsWindowVisible([IntPtr]$_.hwnd) }).hwnd
    }
    if(!$menu){throw 'Native shortcut menu did not appear'}
    return $menu
}
$app = $null
try {
    $notebook = if ($DataDirectory) { Join-Path $DataDirectory 'notes.json' } else { Join-Path $BuildDirectory 'product-check-tray/notes.json' }
    if (!(Test-Path -LiteralPath $notebook)) {
        # The documented Release command accepts a fresh synthetic data folder.
        # Seed through the real editor, then restart to test actual persistence.
        $app = Start-Process $exe -ArgumentList '--tray-check' -PassThru
        Start-Sleep -Milliseconds 1500
        $main=(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch').hwnd
        Invoke-Ui invoke NewNote -w $main | Out-Null
        Start-Sleep -Milliseconds 400
        $fixture=(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').hwnd
        Invoke-Ui set-value NoteText 'Physical tray acceptance fixture.' -w $fixture | Out-Null
        Invoke-Ui invoke Quit -w $main | Out-Null
        if (!$app.WaitForExit(5000)) { throw 'Fixture did not quit cleanly.' }
        $app = $null
    }
    $saved = Get-Content $notebook -Raw | ConvertFrom-Json
    $expectedNotes = @($saved.Notes | Where-Object { !$_.DeletedAt }).Count
    $expectedUndo = @($saved.Notes | Where-Object DeletedAt).Count -gt 0
    $app = Start-Process $exe -ArgumentList '--tray-check' -PassThru
    Start-Sleep -Milliseconds 1500
    $main=(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch').hwnd
    Assert-Check ([bool]$main) 'Cold launch exposes one native shell' $checks
    $shellRect=[ScrunchNative+Rect]::new()
    [ScrunchNative]::GetWindowRect([IntPtr]$main,[ref]$shellRect) | Out-Null
    # Avoid leaving the cursor over taskbar hover flyouts from another test run.
    [ScrunchNative]::SetCursorPos($shellRect.Left+40,$shellRect.Top+15) | Out-Null
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes) 'Saved active notes return after a clean quit' $checks
    $surface=TraySurface
    Save-WindowCapture $surface (Join-Path $evidence 'tray-actual-size.png')
    Assert-Check (((Invoke-Ui inspect -w $surface) -join "`n") -match 'Button "Scrunch"') 'Explorer exposes the Scrunch tooltip/accessibility name' $checks
    Invoke-Ui click Scrunch -w $surface | Out-Null
    Start-Sleep -Milliseconds 250
    Assert-Check ([ScrunchNative]::IsWindowVisible([IntPtr]$main)) 'Physical tray left-click shows the existing shell' $checks
    Save-WindowCapture $main (Join-Path $evidence 'tray-shell.png')
    [ScrunchNative]::SetForegroundWindow([IntPtr]$taskbar) | Out-Null
    Invoke-Ui click 'Notification Chevron' -w $taskbar | Out-Null
    Start-Sleep -Milliseconds 200
    Assert-Check (![ScrunchNative]::IsWindowVisible([IntPtr]$main)) 'Physical click away dismisses the tray-opened shell' $checks
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes) 'Click-away keeps notes visible' $checks
    $menu=Menu
    $tree=(Invoke-Ui inspect -w $menu) -join "`n"
    $tree | Set-Content (Join-Path $evidence 'menu-uia.txt')
    Save-WindowCapture $menu (Join-Path $evidence 'native-menu.png')
    Assert-Check ($tree -match 'New note' -and $tree -match 'Show notes' -and $tree -match 'Undo last discard' -and $tree -match 'Settings' -and $tree -match 'Quit') 'Native menu contains every expected action' $checks
    Assert-Check (($tree -match 'Undo last discard.*disabled') -eq !$expectedUndo) 'Native menu Undo availability matches the saved notebook' $checks
    Invoke-Ui send-keys 'n' -w $main --via send-input | Out-Null
    Start-Sleep -Milliseconds 400
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native menu keyboard New note creates a visible editor' $checks
    $newNote=(Get-ScrunchWindows $app.Id | Where-Object { $_.title -eq 'Scrunch note' -and $_.isForeground }).hwnd
    if(!$newNote){throw 'New note did not focus its native editor'}
    [ScrunchNative]::SendMessageW([IntPtr]$newNote,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    $menu=Menu
    $undoTree=(Invoke-Ui inspect -w $menu) -join "`n"
    Assert-Check ($undoTree -match 'Undo last discard' -and $undoTree -notmatch 'Undo last discard.*disabled') 'Native Undo becomes enabled after a recoverable discard' $checks
    # Classic Win32 menu items have no UIA InvokePattern. Deliver real keyboard
    # input to the open menu; retain the CLI's foreground safety checks.
    Invoke-Ui send-keys 'u' -w $main --via send-input | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native tray Undo restores the discarded physical note' $checks
    $menu=Menu; Invoke-Ui send-keys 's' -w $main --via send-input | Out-Null
    Start-Sleep -Milliseconds 150
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $expectedNotes+1) 'Native Show notes leaves all notes visible' $checks
    $menu=Menu; Invoke-Ui send-keys 'e' -w $main --via send-input | Out-Null
    Start-Sleep -Milliseconds 250
    Assert-Check (((Invoke-Ui inspect -w $main) -join "`n") -match 'Back to notes') 'Native tray Settings opens the existing settings view' $checks
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    [ScrunchNative]::SendMessageW([IntPtr]$main,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Assert-Check (!$app.HasExited -and ![ScrunchNative]::IsWindowVisible([IntPtr]$main)) 'Native shell close hides without terminating the process' $checks
    # Global shortcut delivered with Explorer, a different process, foreground.
    $before=@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count
    Invoke-Ui send-keys 'ctrl+alt+n' -w $taskbar --via send-input | Out-Null
    Start-Sleep -Milliseconds 500
    Assert-Check (@(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch note').Count -eq $before+1) 'Global shortcut works with another application foreground and shell hidden' $checks
    $menu=Menu; Invoke-Ui send-keys 'q' -w $main --via send-input | Out-Null
    Assert-Check ($app.WaitForExit(5000)) 'Native tray Quit terminates the resident process' $checks
    $app=$null
    Assert-Check (!(Test-TrayIconRegistered -Executable $exe)) 'Explicit Quit removes the real notification icon' $checks
    $app=Start-Process $exe -ArgumentList '--tray-check','--startup' -PassThru
    Start-Sleep -Milliseconds 1500
    $restored=Get-ScrunchWindows $app.Id
    Assert-Check (@($restored | Where-Object title -eq 'Scrunch').Count -eq 0 -and @($restored | Where-Object title -eq 'Scrunch note').Count -eq $before+1) 'Quiet startup restores all notes while leaving shell hidden' $checks
    $redirect=Start-Process $exe -ArgumentList '--tray-check' -PassThru
    Assert-Check ($redirect.WaitForExit(5000)) 'Manual launch redirects to the quiet resident process' $checks
    Start-Sleep -Milliseconds 250
    $main=(Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch').hwnd
    Assert-Check ([bool]$main) 'Manual reactivation reveals the quiet shell' $checks
    Invoke-Ui invoke Quit -w $main | Out-Null
    Assert-Check ($app.WaitForExit(5000)) 'Footer Quit also exits cleanly after restart' $checks
    $app=$null
} finally {
    Write-Evidence (Join-Path $evidence 'checks.json') $checks
    Stop-Scrunch -Process $app -Executable $exe -TimeoutMilliseconds 5000 | Out-Null
    $env:SCRUNCH_DATA_DIRECTORY = $originalDataDirectory
}
Write-Host "Desktop evidence: $evidence"
