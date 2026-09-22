param([switch]$Locked)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    $restoreArgs = @('restore', 'tests/Scrunch.Tests/Scrunch.Tests.csproj')
    if ($Locked) { $restoreArgs += '--locked-mode' }
    & dotnet @restoreArgs
    if ($LASTEXITCODE) { throw 'Test restore failed.' }
    dotnet test tests/Scrunch.Tests/Scrunch.Tests.csproj -c Release --no-restore --logger trx
    if ($LASTEXITCODE) { throw 'Headless tests failed.' }
    node tools/scrunchfx-assets/author.mjs --check
    if ($LASTEXITCODE) { throw 'Prepared ScrunchFX assets do not match their generator.' }
    & "$PSScriptRoot/compile-shaders.ps1" -Check
    if ($LASTEXITCODE) { throw 'Compiled ScrunchFX shaders do not match Paper.hlsl.' }
} finally { Pop-Location }
