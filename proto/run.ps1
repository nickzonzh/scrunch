param([switch]$Build, [switch]$Verify, [switch]$Bench, [switch]$VerifyProduct, [switch]$VerifyFx, [switch]$FxLab, [switch]$ShellPreview)
$ErrorActionPreference = 'Stop'
$scrunchOutput = Join-Path $PSScriptRoot 'artifacts/scrunch-shell'
$scrunchExecutable = Join-Path $scrunchOutput 'Scrunch.exe'
if ($Build -or !(Test-Path -LiteralPath $scrunchExecutable)) {
    $runningScrunch = Get-Process -Name Scrunch -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $scrunchExecutable }
    if ($runningScrunch) { throw 'Close the checked Scrunch app and any comparison bench before rebuilding.' }
    dotnet build (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj') -p:Platform=x64 -o $scrunchOutput
    if ($LASTEXITCODE -ne 0) { throw 'Scrunch build failed. See the build output above.' }
}
$scrunchWinApp = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
if (!(Test-Path -LiteralPath $scrunchWinApp)) { throw 'Restore Scrunch first; its existing WinApp SDK package supplies the native run tool.' }
$scrunchArguments = @('run', (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj'), '--arch', 'x64', '--no-build', '-p', "OutDir=$scrunchOutput\", '--detach')
if ($Verify) { $scrunchArguments += @('--args', '--verify') }
elseif ($VerifyProduct) { $scrunchArguments += @('--args', '--verify-product') }
elseif ($VerifyFx) { $scrunchArguments += @('--args', '--verify-fx') }
elseif ($FxLab) { $scrunchArguments += @('--args', '--fx-lab') }
elseif ($ShellPreview) { $scrunchArguments += @('--args', '--shell-preview') }
elseif ($Bench) { $scrunchArguments += @('--args', '--bench') }
& $scrunchWinApp @scrunchArguments
if ($LASTEXITCODE -ne 0) { throw 'Scrunch launch failed. See WinApp output above.' }
Write-Host "Opened $scrunchExecutable"
