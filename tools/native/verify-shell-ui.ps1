param([string]$BuildDirectory,
      [string]$OutputDirectory = (Join-Path $env:TEMP 'scrunch-shell-desktop'))
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/Verify.psm1" -Force
if (!$BuildDirectory) { $BuildDirectory = Join-Path (Get-RepoRoot) 'artifacts/debug' }
$exe = Join-Path $BuildDirectory 'Scrunch.exe'
if (Get-Process Scrunch -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'Close this checked build before the matrix.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$fixture = Join-Path $BuildDirectory 'product-check-shell-visual'
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
# Dedicated DEBUG fixture only. Everyday and interactive preview notes are untouched.
$notes = @(
    @{ Id=[guid]::NewGuid(); Text="Call the dentist`nBook the annual check-up"; Colour='lavender'; X=1000; Y=700; Width=300; Height=320 },
    @{ Id=[guid]::NewGuid(); Text="A little time outside`nFind a new walk for Sunday"; Colour='mint'; X=1050; Y=750; Width=300; Height=320 },
    @{ Id=[guid]::NewGuid(); Text="An idea for later`nKeep the first version simple"; Colour='yellow'; X=1100; Y=800; Width=300; Height=320 }
)
@{ Version=1; Notes=$notes } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture 'notes.json') -Encoding utf8
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
function Settle([int]$milliseconds) {
    $until = [DateTime]::UtcNow.AddMilliseconds($milliseconds)
    while ([DateTime]::UtcNow -lt $until) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
}
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$back = New-Object System.Windows.Forms.Form
$back.Text = 'Scrunch verification background'
$back.FormBorderStyle = 'None'; $back.ShowInTaskbar = $false
$back.StartPosition = 'Manual'; $back.Bounds = New-Object System.Drawing.Rectangle(($area.X+60),($area.Y+60),640,680)
$panel = New-Object System.Windows.Forms.Panel
$panel.Bounds = New-Object System.Drawing.Rectangle(260,0,160,680)
$back.Controls.Add($panel)
$results = @(); $app = $null
try {
    $back.Show()
    foreach ($theme in @('light','dark')) {
        foreach ($background in @('bright','dark','solid-fallback')) {
            $back.BackColor = if ($background -eq 'dark') { [System.Drawing.Color]::FromArgb(20,28,42) } else { [System.Drawing.Color]::FromArgb(247,244,237) }
            $panel.BackColor = if ($background -eq 'dark') { [System.Drawing.Color]::FromArgb(40,70,85) } else { [System.Drawing.Color]::FromArgb(120,180,208) }
            $arguments = @('--shell-preview','--visual-fixture')
            if ($theme -eq 'dark') { $arguments += '--dark' }
            if ($background -eq 'solid-fallback') { $arguments += '--solid-backdrop' }
            # This is the interactive app the user asked to inspect, so show it.
            $app = Start-Process $exe -ArgumentList $arguments -PassThru
            Settle 1800
            $main = (Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch').hwnd
            if (!$main) { throw 'Preview did not expose a Scrunch window' }
            # Preserve native size; place on a controlled real desktop background.
            [ScrunchNative]::SetWindowPos([IntPtr]$main,[IntPtr]::Zero,$back.Left+100,$back.Top+90,0,0,0x41) | Out-Null
            Invoke-Ui focus SearchNotes -w $main | Out-Null
            Settle 700
            Invoke-Ui inspect -w $main | Set-Content (Join-Path $OutputDirectory "$theme-$background-uia.txt")
            $path = Join-Path $OutputDirectory "$theme-$background.png"
            # Screen pixels include DWM/Acrylic and the actual backdrop context.
            Save-WindowCapture $main $path -Margin 20
            $diag = Get-Content (Join-Path $BuildDirectory 'shell-backdrop.json') -Raw | ConvertFrom-Json
            $results += @{theme=$theme; background=$background; backdrop=$diag; screenshot=$path; visualAcceptance='Requires image inspection; capture success is not visual acceptance'}
            Write-Evidence (Join-Path $OutputDirectory 'desktop-matrix.json') $results
            Invoke-Ui invoke Quit -w $main | Out-Null
            if (!$app.WaitForExit(5000)) { throw 'Preview did not exit through Quit' }
            $app = $null
        }
    }
    Write-Evidence (Join-Path $OutputDirectory 'desktop-matrix.json') $results
} finally {
    if ($app -and !$app.HasExited) {
        $main = (Get-ScrunchWindows $app.Id | Where-Object title -eq 'Scrunch').hwnd
        if ($main) {
            $tree = Invoke-Ui inspect -w $main
            if ($tree -match 'Quit Button') { Invoke-Ui invoke Quit -w $main | Out-Null }
            else { $tree | Set-Content (Join-Path $OutputDirectory 'startup-failure-uia.txt') }
        }
    }
    $back.Close(); $back.Dispose()
}
Write-Host "Desktop evidence: $OutputDirectory"
