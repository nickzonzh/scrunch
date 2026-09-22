# A temporary neutral desktop surface for public media. No product code or user data.
Add-Type -AssemblyName System.Windows.Forms
$form = [Windows.Forms.Form]::new()
$form.Text = 'Scrunch media backdrop'
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = [Drawing.Point]::new(250,160)
$form.Size = [Drawing.Size]::new(1500,1000)
$form.BackColor = [Drawing.Color]::FromArgb(238,236,230)
$timer = [Windows.Forms.Timer]::new()
$timer.Interval = 600000
$timer.Add_Tick({ $form.Close() })
$timer.Start()
try { [Windows.Forms.Application]::Run($form) } finally { $timer.Dispose(); $form.Dispose() }
