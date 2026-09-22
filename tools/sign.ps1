param([Parameter(Mandatory)][string[]]$Path, [switch]$Require)
# Authenticode signing hook. SCRUNCH_SIGN_ARGS holds the `signtool sign` arguments
# for whatever certificate source is in use (Azure Trusted Signing dlib, a PFX, an
# HSM). Unset means the release stays unsigned, which release.ps1 reports; -Require
# turns that into a failure for the Inno Setup callback and for CI tag builds.
$ErrorActionPreference = 'Stop'
$arguments = $env:SCRUNCH_SIGN_ARGS
if (!$arguments) {
    if ($Require) { throw 'SCRUNCH_SIGN_ARGS is not set; signing is required here.' }
    Write-Warning 'Signing skipped: SCRUNCH_SIGN_ARGS is not set. Artifacts stay unsigned (docs/RELEASING.md, Signing).'
    return
}
$signtool = $env:SCRUNCH_SIGNTOOL
if (!$signtool) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/10/bin/10.*/x64/signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$signtool -or !(Test-Path -LiteralPath $signtool)) { throw 'signtool.exe not found. Install the Windows SDK or set SCRUNCH_SIGNTOOL.' }
# Split on spaces outside double quotes, then drop the quotes.
$argv = @([regex]::Matches($arguments, '"[^"]*"|\S+') | ForEach-Object { $_.Value.Trim('"') })
foreach ($file in $Path) {
    if (!(Test-Path -LiteralPath $file)) { throw "Cannot sign a missing file: $file" }
    & $signtool sign @argv $file
    if ($LASTEXITCODE) { throw "signtool failed for $file" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid') { throw "Signature on $file is $($signature.Status): $($signature.StatusMessage)" }
    Write-Host "Signed $file as $($signature.SignerCertificate.Subject)"
}
