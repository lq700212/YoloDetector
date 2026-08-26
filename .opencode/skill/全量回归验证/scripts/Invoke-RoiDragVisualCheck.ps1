# ROI drag visual check: new MainForm -> feed test image -> drive selection state -> screenshot
# Press/Drag only (verify Paint overlay); NO MouseUp (avoid writing live esdConfig.json)
$ErrorActionPreference = "Stop"
$bin = "E:\Project\YoloDetector\bin\Debug\net472"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# Load all dependencies first, then the main exe (same directory resolution)
Get-ChildItem "$bin\*.dll" | ForEach-Object {
    try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
}
[Reflection.Assembly]::LoadFrom("$bin\YoloDetector.exe") | Out-Null

$form = New-Object YoloDetector.UI.MainForm
$form.StartPosition = "Manual"
$form.Location = New-Object System.Drawing.Point(50, 50)
$form.TopMost = $true
$form.Show()
$form.Activate()
[System.Windows.Forms.Application]::DoEvents()

# Reflect private control field and feed a gridded test image
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
$pic = $form.GetType().GetField("videoPictureBox", $flags).GetValue($form)

$bmp = New-Object System.Drawing.Bitmap(1280, 720)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(40, 60, 90))
for ($x = 0; $x -lt 1280; $x += 80) { $g.DrawLine([System.Drawing.Pens]::DimGray, $x, 0, $x, 720) }
for ($y = 0; $y -lt 720; $y += 80) { $g.DrawLine([System.Drawing.Pens]::DimGray, 0, $y, 1280, $y) }
$g.Dispose()
$old = $pic.Image
$pic.Image = $bmp
if ($old) { $old.Dispose() }
[System.Windows.Forms.Application]::DoEvents()

# Reflect the ROI state machine and simulate a drag (no release)
$roi = $form.GetType().GetField("_roiSelection", $flags).GetValue($form)
$roi.Press((New-Object System.Drawing.Point(200, 120)))
$roi.Drag((New-Object System.Drawing.Point(700, 420)))
$pic.Invalidate()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 400
[System.Windows.Forms.Application]::DoEvents()

# Screenshot the whole form client area (pictureBox position visible for coordinate check)
$pt = $form.PointToScreen((New-Object System.Drawing.Point(0, 0)))
$snap = New-Object System.Drawing.Bitmap($form.ClientSize.Width, $form.ClientSize.Height)
$gs = [System.Drawing.Graphics]::FromImage($snap)
$gs.CopyFromScreen($pt.X, $pt.Y, 0, 0, $snap.Size)
$gs.Dispose()
$snap.Save("E:\Project\YoloDetector\.opencode\tmp_roi_drag.png", [System.Drawing.Imaging.ImageFormat]::Png)
$snap.Dispose()

Write-Host ("ROI state: IsSelecting=" + $roi.IsSelecting)
Write-Host ("ROI rect: " + $roi.CurrentRectControl.ToString())
Write-Host ("Pic client: " + $pic.ClientSize.ToString())

$form.Close()
$form.Dispose()
Write-Host "DONE"
