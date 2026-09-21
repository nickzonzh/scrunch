param([switch]$Build, [switch]$Verify, [switch]$Bench, [switch]$VerifyProduct, [switch]$VerifyFx, [switch]$FxLab, [switch]$ShellPreview, [switch]$VerifyTray)
$ErrorActionPreference = 'Stop'
$scrunchOutput = Join-Path $PSScriptRoot 'artifacts/scrunch-compact'
$scrunchExecutable = Join-Path $scrunchOutput 'Scrunch.exe'
if ($Build -or !(Test-Path -LiteralPath $scrunchExecutable)) {
    $runningScrunch = Get-Process -Name Scrunch -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $scrunchExecutable }
    if ($runningScrunch) { throw 'Use Quit in the Scrunch tray menu or shell before rebuilding. Closing the shell only hides it.' }
    dotnet build (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj') -p:Platform=x64 -o $scrunchOutput
    if ($LASTEXITCODE -ne 0) { throw 'Scrunch build failed. See the build output above.' }
}
$scrunchWinApp = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
if (!(Test-Path -LiteralPath $scrunchWinApp)) { throw 'Restore Scrunch first; its existing WinApp SDK package supplies the native run tool.' }
$scrunchArguments = @('run', (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj'), '--arch', 'x64', '--no-build', '-p', "OutDir=$scrunchOutput\", '--detach')
if ($Verify) { $scrunchArguments += @('--args', '--verify') }
elseif ($VerifyTray) { $scrunchArguments += @('--args', '--tray-check --verify-tray') }
elseif ($VerifyProduct) { $scrunchArguments += @('--args', '--verify-product') }
elseif ($VerifyFx) { $scrunchArguments += @('--args', '--verify-fx') }
elseif ($FxLab) { $scrunchArguments += @('--args', '--fx-lab') }
elseif ($ShellPreview) { $scrunchArguments += @('--args', '--shell-preview') }
elseif ($Bench) { $scrunchArguments += @('--args', '--bench') }
& $scrunchWinApp @scrunchArguments
if ($LASTEXITCODE -ne 0) { throw 'Scrunch launch failed. See WinApp output above.' }
Write-Host "Opened $scrunchExecutable"
