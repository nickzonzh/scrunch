# Shared helpers for Scrunch's native desktop verification scripts. Every script in
# tools/native and the three tools/verify-*.ps1 lifecycle scripts imports this module
# instead of keeping a private copy of the WinApp wrapper, launch/quit sequence,
# evidence writer and check recorder.

$script:WinAppCli = $null

# Repository root, independent of which directory the importing script lives in:
# this module always sits in <root>/tools/native.
function Get-RepoRoot { Split-Path (Split-Path $PSScriptRoot) }

# The WinApp CLI ships inside the Windows SDK build tools package that Scrunch
# already restores; there is no separate tool to install.
function Get-WinAppCli {
    if (!$script:WinAppCli) {
        $path = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
        if (!(Test-Path -LiteralPath $path)) {
            throw "Restore Scrunch first; its WinApp SDK package supplies the native verification tool ($path)."
        }
        $script:WinAppCli = $path
    }
    return $script:WinAppCli
}

# Throwing wrapper around `winapp ui`. Arguments are passed through verbatim so
# call sites read exactly like the CLI: Invoke-Ui invoke Quit -w $main
function Invoke-Ui {
    $cli = Get-WinAppCli
    $result = & $cli ui @args
    if ($LASTEXITCODE -ne 0) { throw "Native UI failed: $args`n$result" }
    return $result
}

function Get-ScrunchWindows {
    param([Parameter(Mandatory)][int]$ProcessId)
    return @(Invoke-Ui list-windows -a $ProcessId --json | ConvertFrom-Json)
}

# Starts the given executable against an isolated, process-only data directory and
# waits for its shell window. -Quiet is for --startup launches, which deliberately
# restore notes without showing a shell. Callers save and restore the original
# SCRUNCH_DATA_DIRECTORY themselves, because the whole script shares one value.
function Start-ScrunchIsolated {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [string]$DataDirectory,
        [string[]]$ArgumentList = @(),
        [string]$WindowTitle = 'Scrunch',
        [int]$SettleMilliseconds = 1600,
        [int]$TimeoutMilliseconds = 8000,
        [switch]$Quiet
    )
    if (!$DataDirectory) {
        $DataDirectory = Join-Path (Get-RepoRoot) ('artifacts/verify-data-' + [guid]::NewGuid().ToString('N'))
    }
    $DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
    New-Item -ItemType Directory -Force $DataDirectory | Out-Null
    $env:SCRUNCH_DATA_DIRECTORY = $DataDirectory
    $process = if ($ArgumentList.Count) { Start-Process $Executable -ArgumentList $ArgumentList -PassThru }
               else { Start-Process $Executable -PassThru }
    Start-Sleep -Milliseconds $SettleMilliseconds
    if ($process.HasExited) { throw "Scrunch exited immediately: $Executable" }
    $main = $null
    if (!$Quiet) {
        $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
        while (!$main -and [DateTime]::UtcNow -lt $deadline) {
            $main = (Get-ScrunchWindows $process.Id | Where-Object title -eq $WindowTitle | Select-Object -First 1).hwnd
            if (!$main) { Start-Sleep -Milliseconds 200 }
        }
        if (!$main) { throw "Scrunch did not expose its '$WindowTitle' window." }
    } else {
        $main = (Get-ScrunchWindows $process.Id | Where-Object title -eq $WindowTitle | Select-Object -First 1).hwnd
    }
    return [pscustomobject]@{ Process = $process; MainWindow = $main; DataDirectory = $DataDirectory }
}

# Quits through the real UI. A hidden shell is revealed first with the documented
# single-instance redirect when an executable is supplied; WaitForExit is the
# fallback verdict, and an app that ignores Quit is reported, never force-killed
# silently.
function Stop-Scrunch {
    param(
        [AllowNull()]$Process,
        $MainWindow,
        [string]$Executable,
        [int]$TimeoutMilliseconds = 6000
    )
    if (!$Process -or $Process.HasExited) { return $true }
    if (!$MainWindow) { $MainWindow = (Get-ScrunchWindows $Process.Id | Where-Object title -eq 'Scrunch' | Select-Object -First 1).hwnd }
    if (!$MainWindow -and $Executable) {
        $redirect = Start-Process $Executable -PassThru
        $redirect.WaitForExit($TimeoutMilliseconds) | Out-Null
        Start-Sleep -Milliseconds 250
        $MainWindow = (Get-ScrunchWindows $Process.Id | Where-Object title -eq 'Scrunch' | Select-Object -First 1).hwnd
    }
    if ($MainWindow) {
        # Settings does not contain the footer; return home before quitting.
        if (((Invoke-Ui inspect -w $MainWindow) -join "`n") -match 'Back to notes') { Invoke-Ui invoke BackToNotes -w $MainWindow | Out-Null }
        Invoke-Ui invoke Quit -w $MainWindow | Out-Null
    }
    if ($Process.WaitForExit($TimeoutMilliseconds)) { return $true }
    Write-Warning 'Scrunch did not exit through Quit; the process still needs attention.'
    return $false
}

# Always writes a document, including for an empty check list: a missing evidence
# file is indistinguishable from a run that never started.
function Write-Evidence {
    param(
        [Parameter(Mandatory)][string]$Path,
        [AllowNull()]$Value,
        [int]$Depth = 6
    )
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
    Set-Content -LiteralPath $Path -Value (ConvertTo-Json -InputObject $Value -Depth $Depth)
}

# Records a passing acceptance check, or fails the run with the same sentence.
function Assert-Check {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Label,
        [AllowNull()]$Checks
    )
    if (!$Condition) { throw $Label }
    if ($null -ne $Checks) { [void]$Checks.Add($Label) }
    Write-Host "PASS: $Label"
}

if (!('ScrunchNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ScrunchNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct IconId { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
 [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref IconId id, out Rect rect);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
 // Ctrl+Alt+N delivered by the OS, not by the application under test.
 public static void NewNoteKey() { keybd_event(0x11,0,0,0); keybd_event(0x12,0,0,0); keybd_event(0x4e,0,0,0); keybd_event(0x4e,0,2,0); keybd_event(0x12,0,2,0); keybd_event(0x11,0,2,0); }
}
'@
}

# Explorer binds an unsigned notification GUID to the executable path; this is the
# same derivation Scrunch uses, so it proves the real icon is registered.
function Test-TrayIconRegistered {
    param([Parameter(Mandatory)][string]$Executable)
    $hash = [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes('Scrunch.NotificationIcon.v1|' + [IO.Path]::GetFullPath($Executable).ToUpperInvariant()))
    $icon = [ScrunchNative+IconId]::new()
    $icon.Size = [Runtime.InteropServices.Marshal]::SizeOf($icon)
    $icon.Guid = [guid]::new([byte[]]$hash[0..15])
    $rect = [ScrunchNative+Rect]::new()
    return [ScrunchNative]::Shell_NotifyIconGetRect([ref]$icon, [ref]$rect) -eq 0
}

# Real screen pixels, including DWM composition and whatever is behind the window.
function Save-WindowCapture {
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Path,
        [int]$Margin = 0
    )
    Add-Type -AssemblyName System.Drawing
    $rect = [ScrunchNative+Rect]::new()
    [ScrunchNative]::GetWindowRect([IntPtr]$Window, [ref]$rect) | Out-Null
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
    $bitmap = [Drawing.Bitmap]::new(($rect.Right - $rect.Left + 2 * $Margin), ($rect.Bottom - $rect.Top + 2 * $Margin))
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(($rect.Left - $Margin), ($rect.Top - $Margin), 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

Export-ModuleMember -Function Get-RepoRoot, Get-WinAppCli, Invoke-Ui, Get-ScrunchWindows,
    Start-ScrunchIsolated, Stop-Scrunch, Write-Evidence, Assert-Check, Test-TrayIconRegistered, Save-WindowCapture
