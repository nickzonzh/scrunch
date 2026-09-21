$ErrorActionPreference = 'Stop'
$revision = 'f84648b001dd13becda7a629ab5398cecfe3a252'
$destination = Join-Path $PSScriptRoot '../../proto/artifacts/nootfx-source'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$files = @{
    'paper.fbx' = 'vat/geo/vertex_animation_textures1_mesh.fbx'
    'position.exr' = 'vat/tex/vertex_animation_textures1_pos.exr'
    'LICENSE' = 'LICENSE'
}
foreach ($entry in $files.GetEnumerator()) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/item-develop/paper-crumple-demo/$revision/$($entry.Value)" -OutFile (Join-Path $destination $entry.Key)
}
$hashes = @{
    'paper.fbx' = '1F8882CB799EC3C2788D4F65E99521CA3ACD69D3A4CC1903B22BD19250E721A1'
    'position.exr' = '88EA8F26C5F6B261D0F21A8703243D4D0D6599091ECEC35B7C4F417C965AAFF8'
}
foreach ($entry in $hashes.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath (Join-Path $destination $entry.Key) -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Source asset hash mismatch: $($entry.Key)"
    }
}
Write-Host 'Pinned MIT source assets verified. Run npm ci --ignore-scripts, then node prepare.mjs here.'
