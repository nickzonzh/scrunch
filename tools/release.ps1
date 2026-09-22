param([string]$InnoCompiler, [switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a stable semantic version in Directory.Build.props.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Tag and application version disagree.' }
$release = Join-Path $root "artifacts/release/$version"
$publish = Join-Path $release 'publish/win-x64'
if (Test-Path $release) {
    $resolved = [IO.Path]::GetFullPath($release)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/release')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe output path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Force $publish | Out-Null
Push-Location $root
try {
    if (!$SkipChecks) { & "$PSScriptRoot/check.ps1" -Locked }
    dotnet restore proto/Scrunch/Scrunch.csproj -r win-x64 -p:Platform=x64 --locked-mode
    if ($LASTEXITCODE) { throw 'Application restore failed.' }
    dotnet publish proto/Scrunch/Scrunch.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile= -p:ContinuousIntegrationBuild=true --no-restore -o $publish
    if ($LASTEXITCODE) { throw 'Release publish failed.' }
    & "$PSScriptRoot/write-notices.ps1" -OutputDirectory $publish
    Copy-Item LICENSE, THIRD_PARTY_NOTICES.md -Destination $publish
    & "$PSScriptRoot/verify-payload.ps1" -Directory $publish
    $zip = Join-Path $release "Scrunch-$version-win-x64.zip"
    Compress-Archive -Path "$publish/*" -DestinationPath $zip -CompressionLevel Optimal
    if (!$InnoCompiler) { $InnoCompiler = & "$PSScriptRoot/get-inno.ps1" }
    & $InnoCompiler /Qp "/DAppVersion=$version" "/DPublishDir=$publish" "/DArtifactDir=$release" packaging/Scrunch.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    $artifacts = @(Get-ChildItem $release -File | Where-Object Extension -in '.exe','.zip' | Sort-Object Name)
    $artifacts | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
        Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
    $artifacts | Select-Object Name, Length
    Write-Host "Release artifacts: $release"
} finally { Pop-Location }
