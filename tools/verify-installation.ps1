param([switch]$UseExistingTestInstall)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$release = Join-Path $root "artifacts/release/$version"
$evidence = Join-Path $root 'artifacts/installed-verification'
$installed = Join-Path $env:LOCALAPPDATA 'Programs/Scrunch'
$exe = Join-Path $installed 'Scrunch.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Scrunch.lnk'
$cli = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue) { throw 'Quit Scrunch before installation verification.' }
if ((Test-Path $installed) -and !$UseExistingTestInstall) { throw 'An installation already exists. Use a disposable Windows profile, or review and explicitly identify it as a test installation.' }
if ($UseExistingTestInstall) {
    $prior = Get-Content (Join-Path $evidence 'original-state.json') -Raw | ConvertFrom-Json
    if ($prior.installExisted -ne $false) { throw 'No evidence that the existing install belongs to this test.' }
}
New-Item -ItemType Directory -Force $evidence | Out-Null
$data = Join-Path $evidence ('lifecycle-data-' + [guid]::NewGuid().ToString('N'))
$originalEnvironment = $env:SCRUNCH_DATA_DIRECTORY
$env:SCRUNCH_DATA_DIRECTORY = $data
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$approvalPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
$originalRun = if ($run) { $run.GetValue('Scrunch', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { $null }
$originalRunKind = if ($null -ne $originalRun) { $run.GetValueKind('Scrunch') } else { [Microsoft.Win32.RegistryValueKind]::String }
if ($run) { $run.Dispose() }
$approval = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($approvalPath)
$originalApproval = if ($approval) { $approval.GetValue('Scrunch'); $approval.Dispose() } else { $null }
$personalHashes = @{}
foreach ($folder in @('Noot','Scrunch')) {
    $path = Join-Path $env:LOCALAPPDATA "$folder/notes.json"
    $personalHashes[$path] = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
}
$checks = [Collections.Generic.List[string]]::new()
$app = $null
function Check([bool]$value,[string]$label) { if (!$value) { throw $label }; $checks.Add($label); Write-Host "PASS: $label" }
function Ui { $result = & $cli ui @args; if ($LASTEXITCODE) { throw "Native UI failed: $args" }; return $result }
function Windows { @(Ui list-windows -a $app.Id --json | ConvertFrom-Json) }
function Main { (Windows | Where-Object title -eq 'Scrunch' | Select-Object -First 1).hwnd }
function Notes { @(Windows | Where-Object title -eq 'Scrunch note') }
function Start-App([switch]$Quiet,[switch]$FromStartMenu) {
    if ($FromStartMenu) {
        Start-Process $shortcut
        Start-Sleep -Milliseconds 1600
        $script:app = Get-Process Scrunch | Where-Object Path -eq $exe
    } else {
        $script:app = if ($Quiet) { Start-Process $exe -ArgumentList '--startup' -PassThru } else { Start-Process $exe -PassThru }
        Start-Sleep -Milliseconds 1600
    }
    if (!$script:app -or $script:app.HasExited) { throw 'Installed app did not remain running.' }
}
function Show-Main {
    if (!(Main)) {
        $redirect = Start-Process $exe -PassThru
        if (!$redirect.WaitForExit(8000)) { throw 'Single-instance redirect timed out.' }
        Start-Sleep -Milliseconds 300
    }
    return Main
}
function Quit-App {
    if ($script:app -and !$script:app.HasExited) {
        $main = Show-Main
        # Settings does not contain the footer; return home if needed.
        if (((Ui inspect -w $main) -join "`n") -match 'Back to notes') { Ui invoke BackToNotes -w $main | Out-Null }
        Ui invoke Quit -w $main | Out-Null
        if (!$script:app.WaitForExit(8000)) { throw 'Installed app did not quit cleanly.' }
    }
    $script:app = $null
}
function Install {
    $setup = Join-Path $release "Scrunch-$version-Setup.exe"
    $log = Join-Path $evidence ('setup-' + [guid]::NewGuid().ToString('N') + '.log')
    $process = Start-Process $setup -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Exact final installer exits successfully'
    Check ((Get-FileHash $exe).Hash -eq (Get-FileHash (Join-Path $release 'publish/win-x64/Scrunch.exe')).Hash -and
        (Get-FileHash (Join-Path $installed 'Scrunch.dll')).Hash -eq (Get-FileHash (Join-Path $release 'publish/win-x64/Scrunch.dll')).Hash) 'Installed executable and managed payload match the final release'
}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class InstalledSmoke {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr w,uint m,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr w,uint m,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] static extern void keybd_event(byte k,byte scan,uint flags,nuint extra);
 public static void NewNoteKey(){keybd_event(0x11,0,0,0);keybd_event(0x12,0,0,0);keybd_event(0x4e,0,0,0);keybd_event(0x4e,0,2,0);keybd_event(0x12,0,2,0);keybd_event(0x11,0,2,0);}
}
'@
$errorText = $null
try {
    Install
    Check ((Test-Path $shortcut) -and (Get-ItemPropertyValue HKCU:/Software/Scrunch -Name InstallLocation) -eq $installed) 'Stable per-user installation and Start Menu entry exist'
    Start-App -FromStartMenu
    $main = Main
    Check ([bool]$main) 'Final installed app opens from its Start Menu shortcut'
    Ui invoke NewNote -w $main | Out-Null
    Start-Sleep -Milliseconds 400
    $note = (Notes | Select-Object -First 1).hwnd
    Check ([bool]$note) 'Installed app creates a native note'
    Ui set-value NoteText 'Release installation fixture. Preserve this note.' -w $note | Out-Null
    Start-Sleep -Milliseconds 800
    Check ((Get-Content (Join-Path $data 'notes.json') -Raw) -match 'Preserve this note') 'Installed editor autosaves synthetic text'
    Ui invoke Settings -w $main | Out-Null
    Check (((Ui inspect -w $main --depth 8) -join "`n") -match 'Ctrl \+ Alt \+ N · New note while Scrunch is running') 'Installed app confirms global shortcut registration'
    Ui invoke BackToNotes -w $main | Out-Null
    [InstalledSmoke]::SendMessageW([IntPtr]$main,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Check (!$app.HasExited -and ![InstalledSmoke]::IsWindowVisible([IntPtr]$main) -and (Notes).Count -eq 1) 'Closing the shell preserves resident app and note'
    [InstalledSmoke]::NewNoteKey()
    Start-Sleep -Milliseconds 700
    Check ((Notes).Count -eq 2) 'OS-delivered Ctrl+Alt+N creates a note with shell hidden'
    $main = Show-Main
    Ui invoke Settings -w $main | Out-Null
    Check (((Ui get-property StartWithWindows -w $main) -join "`n") -match 'ToggleState: Off') 'Start with Windows defaults to off'
    Ui invoke StartWithWindows -w $main | Out-Null
    Check ((Get-ItemPropertyValue "HKCU:/$runPath" -Name Scrunch) -eq ('"'+$exe+'" --startup')) 'Startup uses one quoted installed path plus --startup'
    Quit-App
    $saved = (Get-FileHash (Join-Path $data 'notes.json')).Hash
    Install
    Check ((Get-FileHash (Join-Path $data 'notes.json')).Hash -eq $saved) 'Reinstall preserves notebook bytes'
    Check ((Get-ItemPropertyValue "HKCU:/$runPath" -Name Scrunch) -eq ('"'+$exe+'" --startup')) 'Reinstall preserves the existing startup opt-in'
    Start-App -Quiet
    Check (!(Main) -and (Notes).Count -eq 2) 'Final installed --startup restores notes with the shell hidden'
    $main = Show-Main
    Check ([bool]$main) 'Second manual launch redirects to the quiet resident instance'
    Ui invoke Settings -w $main | Out-Null
    Ui invoke StartWithWindows -w $main | Out-Null
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Check ($null -eq $key.GetValue('Scrunch')) 'Disabling startup removes registration'; $key.Dispose()
    # Emulate the Windows Startup Apps disabled state, restoring the original
    # bytes in finally. A removed Run value must still be registrable afterwards.
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    $disabled=[byte[]]::new(12);$disabled[0]=3
    $key.SetValue('Scrunch',$disabled,[Microsoft.Win32.RegistryValueKind]::Binary);$key.Dispose()
    Ui invoke BackToNotes -w $main | Out-Null
    Ui invoke Settings -w $main | Out-Null
    Ui invoke StartWithWindows -w $main | Out-Null
    Check (((Ui inspect -w $main --depth 8) -join "`n") -match 'Disabled in Windows Startup Apps') 'Windows-disabled startup remains visibly disabled after re-registration'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($approvalPath)
    Check ($key.GetValue('Scrunch')[0] -eq 3) 'App preserves Windows startup approval state';$key.Dispose()
    Ui invoke 'Remove startup entry' -w $main | Out-Null
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Check ($null -eq $key.GetValue('Scrunch')) 'A Windows-disabled Run entry can be removed';$key.Dispose()
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    $key.DeleteValue('Scrunch',$false);$key.Dispose()
    Ui invoke BackToNotes -w $main | Out-Null
    Ui invoke Settings -w $main | Out-Null
    # Re-enable so uninstall proves it removes an actual opt-in registration.
    Ui invoke StartWithWindows -w $main | Out-Null
    Quit-App
    $saved = (Get-FileHash (Join-Path $data 'notes.json')).Hash
    $uninstall = Start-Process (Join-Path $installed 'unins000.exe') -WindowStyle Hidden -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    Check ($uninstall.ExitCode -eq 0 -and !(Test-Path $exe) -and !(Test-Path $shortcut)) 'Uninstall removes binaries and the Start Menu entry'
    Check ((Get-FileHash (Join-Path $data 'notes.json')).Hash -eq $saved) 'Uninstall preserves notebook bytes'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Check ($null -eq $key.GetValue('Scrunch')) 'Uninstall removes an enabled startup registration'; $key.Dispose()
    foreach ($path in $personalHashes.Keys) {
        $current = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
        Check ($current -eq $personalHashes[$path]) 'Personal notebook remains untouched'
    }
} catch { $errorText = $_.ToString(); throw }
finally {
    try { Quit-App } catch { Write-Warning "Test process needs attention: $_" }
    $env:SCRUNCH_DATA_DIRECTORY = $originalEnvironment
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
    if ($null -eq $originalRun) { $key.DeleteValue('Scrunch',$false) } else { $key.SetValue('Scrunch',$originalRun,$originalRunKind) }; $key.Dispose()
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    if ($null -eq $originalApproval) { $key.DeleteValue('Scrunch',$false) } else { $key.SetValue('Scrunch',[byte[]]$originalApproval,[Microsoft.Win32.RegistryValueKind]::Binary) }; $key.Dispose()
    @{ passed=($null -eq $errorText); checks=$checks; error=$errorText; dataDirectory=$data; version=$version } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'lifecycle.json')
}
