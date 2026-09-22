param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$assets = Get-Content (Join-Path $root 'src/Scrunch/obj/project.assets.json') -Raw | ConvertFrom-Json
$packages = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
$builder = [Text.StringBuilder]::new()
[void]$builder.AppendLine("Scrunch third-party dependency licences`nGenerated from the restored dependency graph. Build-only packages are excluded.`n")
$seen = [Collections.Generic.HashSet[string]]::new()
function Append-License([string]$Label, [string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { throw "Missing licence: $Label" }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    [void]$builder.AppendLine("`n===== $Label =====")
    if ($seen.Add($hash)) { [void]$builder.AppendLine([IO.File]::ReadAllText($Path)) }
    else { [void]$builder.AppendLine("Same full licence/notice text reproduced above (SHA256 $hash).") }
}
foreach ($library in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($library.Name -match '^Microsoft.Windows.SDK.BuildTools' -or $library.Value.type -eq 'project') { continue }
    $directory = Join-Path $packages $library.Value.path
    $notices = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match 'license|notice|third')
    if ($notices.Count) {
        foreach ($notice in $notices) { Append-License "$($library.Name) / $($notice.Name)" $notice.FullName }
    } elseif ($library.Name -like 'SharpGen.*') {
        Append-License $library.Name (Join-Path $root 'docs/licenses/SharpGen-MIT.txt')
    } elseif ($library.Name -like 'Vortice.Mathematics/*') {
        Append-License $library.Name (Join-Path $root 'docs/licenses/Vortice.Mathematics-MIT.txt')
    } elseif ($library.Name -like 'Vortice.*') {
        Append-License $library.Name (Join-Path $root 'src/Scrunch/Assets/ScrunchFX/Vortice-LICENSE.txt')
    } else { throw "Unreviewed dependency licence: $($library.Name)" }
}
$runtime = Get-Content (Join-Path $OutputDirectory 'Scrunch.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $pack = Join-Path $packages ($framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version)
    Append-License "$($framework.name) $($framework.version) licence" (Join-Path $pack 'LICENSE.TXT')
    Append-License "$($framework.name) $($framework.version) third-party notices" (Join-Path $pack 'THIRD-PARTY-NOTICES.TXT')
}
# The Windows SDK .NET projection is supplied by the SDK targeting pack, outside
# the ordinary NuGet library graph. Preserve Microsoft's SDK distribution terms.
Append-License 'Windows SDK .NET projection distribution terms' (Join-Path $root 'docs/licenses/Windows-SDK.txt')
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'DEPENDENCY_NOTICES.txt'), $builder.ToString(), [Text.UTF8Encoding]::new($false))
