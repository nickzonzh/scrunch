param([switch]$Locked)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    foreach ($name in @('Storage', 'Geometry', 'Fx', 'Tray')) {
        $project = "proto/Scrunch.$name`Checks"
        $restoreArgs = @('restore', $project)
        if ($Locked) { $restoreArgs += '--locked-mode' }
        & dotnet @restoreArgs
        if ($LASTEXITCODE) { throw "$name restore failed." }
        dotnet run --project $project -c Release --no-restore
        if ($LASTEXITCODE) { throw "$name checks failed." }
    }
    node tools/scrunchfx-assets/author.mjs --check
    if ($LASTEXITCODE) { throw 'Prepared ScrunchFX assets do not match their generator.' }
} finally { Pop-Location }
