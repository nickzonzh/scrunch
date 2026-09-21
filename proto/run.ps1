param([switch]$Build, [switch]$Verify, [switch]$Bench, [switch]$VerifyProduct, [switch]$VerifyFx, [switch]$FxLab)
$ErrorActionPreference = 'Stop'
$nootOutput = Join-Path $PSScriptRoot 'artifacts/nootfx-variation-final'
$nootExecutable = Join-Path $nootOutput 'Noot.Proto.exe'
if ($Build -or !(Test-Path -LiteralPath $nootExecutable)) {
    $runningNoot = Get-Process -Name Noot.Proto -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $nootExecutable }
    if ($runningNoot) { throw 'Close the checked Noot app and any comparison bench before rebuilding.' }
    dotnet build (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj') -p:Platform=x64 -o $nootOutput
    if ($LASTEXITCODE -ne 0) { throw 'Noot build failed. See the build output above.' }
}
$nootWinApp = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe'
if (!(Test-Path -LiteralPath $nootWinApp)) { throw 'Restore Noot first; its existing WinApp SDK package supplies the native run tool.' }
$nootArguments = @('run', (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj'), '--arch', 'x64', '--no-build', '-p', "OutDir=$nootOutput\", '--detach')
if ($Verify) { $nootArguments += @('--args', '--verify') }
elseif ($VerifyProduct) { $nootArguments += @('--args', '--verify-product') }
elseif ($VerifyFx) { $nootArguments += @('--args', '--verify-fx') }
elseif ($FxLab) { $nootArguments += @('--args', '--fx-lab') }
elseif ($Bench) { $nootArguments += @('--args', '--bench') }
& $nootWinApp @nootArguments
if ($LASTEXITCODE -ne 0) { throw 'Noot launch failed. See WinApp output above.' }
Write-Host "Opened $nootExecutable"
