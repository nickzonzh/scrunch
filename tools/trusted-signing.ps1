param([Parameter(Mandatory)][string]$Endpoint,
      [Parameter(Mandatory)][string]$Account,
      [Parameter(Mandatory)][string]$Profile)
# Prepares SCRUNCH_SIGN_ARGS for Azure Trusted Signing: pins and unpacks the
# Microsoft.Trusted.Signing.Client dlib so signtool (and therefore Inno Setup)
# can sign through the Azure identity already logged in (`az login` locally,
# azure/login in CI). Sets the variable for this process, and appends it to
# $GITHUB_ENV when running under GitHub Actions. Dot-source it locally:
#   . ./tools/trusted-signing.ps1 -Endpoint ... -Account ... -Profile ...
$ErrorActionPreference = 'Stop'
$version = '1.0.95'
$sha = '3BFCF1E0A3CB42AF1692F0A8ED45C15DE070C2DE86F28A59B2795D904D8A920F'
$cache = Join-Path (Split-Path $PSScriptRoot) "tools/.cache/trusted-signing-client-$version"
$dlib = Join-Path $cache 'bin/x64/Azure.CodeSigning.Dlib.dll'
if (!(Test-Path -LiteralPath $dlib)) {
    $package = "$cache.zip"
    New-Item -ItemType Directory -Force (Split-Path $package) | Out-Null
    Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.trusted.signing.client/$version/microsoft.trusted.signing.client.$version.nupkg" -OutFile $package
    if ((Get-FileHash -LiteralPath $package).Hash -ne $sha) { Remove-Item $package; throw 'Microsoft.Trusted.Signing.Client hash mismatch.' }
    Expand-Archive -LiteralPath $package -DestinationPath $cache -Force
    Remove-Item $package
    if (!(Test-Path -LiteralPath $dlib)) { throw 'Azure.CodeSigning.Dlib.dll not found in the client package.' }
}
$metadata = Join-Path $cache 'metadata.json'
@{ Endpoint = $Endpoint; CodeSigningAccountName = $Account; CertificateProfileName = $Profile } | ConvertTo-Json | Set-Content -LiteralPath $metadata
$signArgs = "/fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib `"$dlib`" /dmdf `"$metadata`""
$env:SCRUNCH_SIGN_ARGS = $signArgs
if ($env:GITHUB_ENV) { "SCRUNCH_SIGN_ARGS=$signArgs" >> $env:GITHUB_ENV }
Write-Host "SCRUNCH_SIGN_ARGS set for Trusted Signing account '$Account', profile '$Profile'."
