param([switch]$UseExistingTestInstall)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/native/Verify.psm1" -Force
$root = Get-RepoRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$release = Join-Path $root "artifacts/release/$version"
$evidence = Join-Path $root 'artifacts/installed-verification'
$installed = Join-Path $env:LOCALAPPDATA 'Programs/Scrunch'
$exe = Join-Path $installed 'Scrunch.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Scrunch.lnk'
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
# A failed run leaves the test installation behind; this record is what lets the
# next run identify it as ours instead of a personal install.
if (!$UseExistingTestInstall) {
    Write-Evidence (Join-Path $evidence 'original-state.json') @{
        installExisted=$false; run=$originalRun; approval=$originalApproval; startedAt=[DateTime]::UtcNow.ToString('o')
    }
}
$personalHashes = @{}
foreach ($folder in @('Noot','Scrunch')) {
    $path = Join-Path $env:LOCALAPPDATA "$folder/notes.json"
    $personalHashes[$path] = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
}
$checks = [Collections.Generic.List[string]]::new()
$app = $null
function Windows { @(Get-ScrunchWindows $app.Id) }
function Main { (Windows | Where-Object title -eq 'Scrunch' | Select-Object -First 1).hwnd }
function Notes { @(Windows | Where-Object title -eq 'Scrunch note') }
function Start-App([switch]$Quiet,[switch]$FromStartMenu) {
    if ($FromStartMenu) {
        Start-Process $shortcut
        Start-Sleep -Milliseconds 1600
        $script:app = Get-Process Scrunch | Where-Object Path -eq $exe
    } else {
        $arguments = if ($Quiet) { @('--startup') } else { @() }
        $script:app = (Start-ScrunchIsolated -Executable $exe -DataDirectory $data -ArgumentList $arguments -Quiet:$Quiet).Process
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
    if (!(Stop-Scrunch -Process $script:app -Executable $exe -TimeoutMilliseconds 8000)) { throw 'Installed app did not quit cleanly.' }
    $script:app = $null
}
function Install {
    $setup = Join-Path $release "Scrunch-$version-Setup.exe"
    $log = Join-Path $evidence ('setup-' + [guid]::NewGuid().ToString('N') + '.log')
    $process = Start-Process $setup -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -Wait -PassThru
    Assert-Check ($process.ExitCode -eq 0) 'Exact final installer exits successfully' $checks
    Assert-Check ((Get-FileHash $exe).Hash -eq (Get-FileHash (Join-Path $release 'publish/win-x64/Scrunch.exe')).Hash -and
        (Get-FileHash (Join-Path $installed 'Scrunch.dll')).Hash -eq (Get-FileHash (Join-Path $release 'publish/win-x64/Scrunch.dll')).Hash) 'Installed executable and managed payload match the final release' $checks
}
$errorText = $null
try {
    Install
    Assert-Check ((Test-Path $shortcut) -and (Get-ItemPropertyValue HKCU:/Software/Scrunch -Name InstallLocation) -eq $installed) 'Stable per-user installation and Start Menu entry exist' $checks
    Start-App -FromStartMenu
    $main = Main
    Assert-Check ([bool]$main) 'Final installed app opens from its Start Menu shortcut' $checks
    Invoke-Ui invoke NewNote -w $main | Out-Null
    Start-Sleep -Milliseconds 400
    $note = (Notes | Select-Object -First 1).hwnd
    Assert-Check ([bool]$note) 'Installed app creates a native note' $checks
    Invoke-Ui set-value NoteText 'Release installation fixture. Preserve this note.' -w $note | Out-Null
    Start-Sleep -Milliseconds 800
    Assert-Check ((Get-Content (Join-Path $data 'notes.json') -Raw) -match 'Preserve this note') 'Installed editor autosaves synthetic text' $checks
    Invoke-Ui invoke Settings -w $main | Out-Null
    Assert-Check (((Invoke-Ui inspect -w $main --depth 8) -join "`n") -match 'Ctrl \+ Alt \+ N · New note while Scrunch is running') 'Installed app confirms global shortcut registration' $checks
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    [ScrunchNative]::SendMessageW([IntPtr]$main,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Assert-Check (!$app.HasExited -and ![ScrunchNative]::IsWindowVisible([IntPtr]$main) -and (Notes).Count -eq 1) 'Closing the shell preserves resident app and note' $checks
    [ScrunchNative]::NewNoteKey()
    Start-Sleep -Milliseconds 700
    Assert-Check ((Notes).Count -eq 2) 'OS-delivered Ctrl+Alt+N creates a note with shell hidden' $checks
    $main = Show-Main
    Invoke-Ui invoke Settings -w $main | Out-Null
    Assert-Check (((Invoke-Ui get-property StartWithWindows -w $main) -join "`n") -match 'ToggleState: Off') 'Start with Windows defaults to off' $checks
    Invoke-Ui invoke StartWithWindows -w $main | Out-Null
    Assert-Check ((Get-ItemPropertyValue "HKCU:/$runPath" -Name Scrunch) -eq ('"'+$exe+'" --startup')) 'Startup uses one quoted installed path plus --startup' $checks
    Quit-App
    $saved = (Get-FileHash (Join-Path $data 'notes.json')).Hash
    Install
    Assert-Check ((Get-FileHash (Join-Path $data 'notes.json')).Hash -eq $saved) 'Reinstall preserves notebook bytes' $checks
    Assert-Check ((Get-ItemPropertyValue "HKCU:/$runPath" -Name Scrunch) -eq ('"'+$exe+'" --startup')) 'Reinstall preserves the existing startup opt-in' $checks
    Start-App -Quiet
    Assert-Check (!(Main) -and (Notes).Count -eq 2) 'Final installed --startup restores notes with the shell hidden' $checks
    $main = Show-Main
    Assert-Check ([bool]$main) 'Second manual launch redirects to the quiet resident instance' $checks
    Invoke-Ui invoke Settings -w $main | Out-Null
    Invoke-Ui invoke StartWithWindows -w $main | Out-Null
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Assert-Check ($null -eq $key.GetValue('Scrunch')) 'Disabling startup removes registration' $checks; $key.Dispose()
    # Emulate the Windows Startup Apps disabled state, restoring the original
    # bytes in finally. A removed Run value must still be registrable afterwards.
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    $disabled=[byte[]]::new(12);$disabled[0]=3
    $key.SetValue('Scrunch',$disabled,[Microsoft.Win32.RegistryValueKind]::Binary);$key.Dispose()
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    Invoke-Ui invoke Settings -w $main | Out-Null
    Invoke-Ui invoke StartWithWindows -w $main | Out-Null
    Assert-Check (((Invoke-Ui inspect -w $main --depth 8) -join "`n") -match 'Disabled in Windows Startup Apps') 'Windows-disabled startup remains visibly disabled after re-registration' $checks
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($approvalPath)
    Assert-Check ($key.GetValue('Scrunch')[0] -eq 3) 'App preserves Windows startup approval state' $checks;$key.Dispose()
    Invoke-Ui invoke 'Remove startup entry' -w $main | Out-Null
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Assert-Check ($null -eq $key.GetValue('Scrunch')) 'A Windows-disabled Run entry can be removed' $checks;$key.Dispose()
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    $key.DeleteValue('Scrunch',$false);$key.Dispose()
    Invoke-Ui invoke BackToNotes -w $main | Out-Null
    Invoke-Ui invoke Settings -w $main | Out-Null
    # Re-enable so uninstall proves it removes an actual opt-in registration.
    Invoke-Ui invoke StartWithWindows -w $main | Out-Null
    Quit-App
    $saved = (Get-FileHash (Join-Path $data 'notes.json')).Hash
    $uninstall = Start-Process (Join-Path $installed 'unins000.exe') -WindowStyle Hidden -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    Assert-Check ($uninstall.ExitCode -eq 0 -and !(Test-Path $exe) -and !(Test-Path $shortcut)) 'Uninstall removes binaries and the Start Menu entry' $checks
    Assert-Check ((Get-FileHash (Join-Path $data 'notes.json')).Hash -eq $saved) 'Uninstall preserves notebook bytes' $checks
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
    Assert-Check ($null -eq $key.GetValue('Scrunch')) 'Uninstall removes an enabled startup registration' $checks; $key.Dispose()
    foreach ($path in $personalHashes.Keys) {
        $current = if (Test-Path $path) { (Get-FileHash $path).Hash } else { $null }
        Assert-Check ($current -eq $personalHashes[$path]) 'Personal notebook remains untouched' $checks
    }
} catch { $errorText = $_.ToString(); throw }
finally {
    try { Quit-App } catch { Write-Warning "Test process needs attention: $_" }
    $env:SCRUNCH_DATA_DIRECTORY = $originalEnvironment
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
    if ($null -eq $originalRun) { $key.DeleteValue('Scrunch',$false) } else { $key.SetValue('Scrunch',$originalRun,$originalRunKind) }; $key.Dispose()
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approvalPath)
    if ($null -eq $originalApproval) { $key.DeleteValue('Scrunch',$false) } else { $key.SetValue('Scrunch',[byte[]]$originalApproval,[Microsoft.Win32.RegistryValueKind]::Binary) }; $key.Dispose()
    Write-Evidence (Join-Path $evidence 'lifecycle.json') @{
        passed=($null -eq $errorText); checks=$checks; error=$errorText; dataDirectory=$data; version=$version
    } -Depth 5
}
