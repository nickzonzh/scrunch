$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot '.cache'
$compiler = Join-Path $cache 'inno-6.7.3/ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Force $cache | Out-Null
$download = Join-Path $cache 'innosetup-6.7.3.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
if ((Get-FileHash $download -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'Inno Setup download hash mismatch.' }
if ((Get-AuthenticodeSignature $download).Status -ne 'Valid') { throw 'Inno Setup signature validation failed.' }
$target = Join-Path $cache 'inno-6.7.3'
$process = Start-Process $download -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS', ('/DIR="' + $target + '"')) -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path $compiler)) { throw 'Inno Setup compiler installation failed.' }
return $compiler
