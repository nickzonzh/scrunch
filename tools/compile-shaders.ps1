param([string]$Fxc, [switch]$Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Join-Path $root 'src/Scrunch/ScrunchFX/Paper.hlsl'
$objects = Join-Path $root 'src/Scrunch/Assets/ScrunchFX/Shaders'
if (!$Fxc) {
    # Newest installed Windows SDK. Stripped shader objects are byte-reproducible
    # for a given compiler, so the version used is recorded in docs/SCRUNCHFX.md.
    $Fxc = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\fxc.exe' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1 -ExpandProperty FullName
}
if (!$Fxc -or !(Test-Path -LiteralPath $Fxc)) { throw 'fxc.exe not found. Install the Windows 10/11 SDK or pass -Fxc.' }
# Entry point -> profile. D3DRenderer loads Shaders/<entry>.cso by these names.
$entries = [ordered]@{
    VS = 'vs_5_0'; PS = 'ps_5_0'
    VSShadow = 'vs_5_0'; PSShadow = 'ps_5_0'
    VSLight = 'vs_5_0'
    VSComposite = 'vs_5_0'; PSComposite = 'ps_5_0'
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ('scrunch-shaders-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    if (!$Check) { New-Item -ItemType Directory -Path $objects -Force | Out-Null }
    $results = foreach ($entry in $entries.Keys) {
        $fresh = Join-Path $staging "$entry.cso"
        # Strip debug info, reflection and private data: the payload ships only
        # the bytecode, and the output does not depend on the source path.
        $log = & $Fxc /nologo /T $entries[$entry] /E $entry /O3 /Qstrip_debug /Qstrip_reflect /Qstrip_priv /Fo $fresh $source 2>&1
        if ($LASTEXITCODE) { throw "fxc failed to compile ${entry}:`n$log" }
        $tracked = Join-Path $objects "$entry.cso"
        if ($Check) {
            if (!(Test-Path -LiteralPath $tracked)) { throw "Missing compiled shader: $entry.cso. Run tools/compile-shaders.ps1." }
            if ((Get-FileHash -LiteralPath $tracked).Hash -ne (Get-FileHash -LiteralPath $fresh).Hash) {
                throw "Compiled shader does not match Paper.hlsl: $entry.cso. Run tools/compile-shaders.ps1."
            }
        } else { Copy-Item -LiteralPath $fresh -Destination $tracked -Force }
        [pscustomobject]@{ entry = $entry; profile = $entries[$entry]; bytes = (Get-Item -LiteralPath $fresh).Length }
    }
} finally { Remove-Item -LiteralPath $staging -Recurse -Force }
$results | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("Windows SDK {0} fxc: {1} shaders, {2} bytes total" -f (Get-Item -LiteralPath $Fxc).Directory.Parent.Name,
    $results.Count, ($results | Measure-Object bytes -Sum).Sum)
