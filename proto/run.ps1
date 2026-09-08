param([switch]$Build, [switch]$Verify, [switch]$Bench, [switch]$VerifyProduct)
$ErrorActionPreference = 'Stop'
$nootOutput = Join-Path $PSScriptRoot 'artifacts/paper-polish-checked'
$nootExecutable = Join-Path $nootOutput 'Noot.Proto.exe'
if ($Build -or !(Test-Path -LiteralPath $nootExecutable)) {
    $runningNoot = Get-Process -Name Noot.Proto -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $nootExecutable }
    if ($runningNoot) { throw 'Close the checked Noot app and any comparison bench before rebuilding.' }
    dotnet build (Join-Path $PSScriptRoot 'Noot.Proto/Noot.Proto.csproj') -p:Platform=x64 -o $nootOutput
    if ($LASTEXITCODE -ne 0) { throw 'Noot build failed. See the build output above.' }
}
if ($Verify) { Start-Process -FilePath $nootExecutable -ArgumentList '--verify' }
elseif ($VerifyProduct) { Start-Process -FilePath $nootExecutable -ArgumentList '--verify-product' }
elseif ($Bench) { Start-Process -FilePath $nootExecutable -ArgumentList '--bench' }
else { Start-Process -FilePath $nootExecutable }
Write-Host "Opened $nootExecutable"
