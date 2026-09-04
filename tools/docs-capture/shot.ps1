# Capture the nfty window to a PNG.
#
# Two things this has to get right. DPI awareness is mandatory: without it the process sees
# virtualized coordinates and both the move and the grab land wrong. And a MoveWindow issued while
# the window is still settling from SW_RESTORE is silently ignored -- hence the settle, move, settle,
# move again. The grab is the WINDOW rect inset by Border.frame's 12px shadow gutter, not the client
# rect, so no desktop bleeds in around the rounded corners.
param([string]$Out, [int]$Width = 1416, [int]$Height = 864, [int]$Gutter = 12)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int t,bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int t,uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out NRECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
}
public struct NRECT { public int Left, Top, Right, Bottom; }
"@
[NWin]::SetProcessDPIAware() | Out-Null

$p = Get-Process Nfty.Desktop -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$hwnd = $p.MainWindowHandle
[NWin]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null   # HWND_TOPMOST, no move/size
[NWin]::SetForegroundWindow($hwnd) | Out-Null

# Place and VERIFY. A single MoveWindow is not enough: issued while the window is still settling it
# is accepted and then undone, and the only way to know is to read the rect back.
$r = New-Object 'NRECT'
for ($i = 0; $i -lt 12; $i++) {
  [NWin]::MoveWindow($hwnd, 60, 60, $Width, $Height, $true) | Out-Null
  Start-Sleep -Milliseconds 350
  [NWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  if (($r.Right - $r.Left) -eq $Width -and ($r.Bottom - $r.Top) -eq $Height) { break }
}
if (($r.Bottom - $r.Top) -ne $Height) { Write-Error "window would not size to ${W}x${H}"; exit 1 }
Start-Sleep -Milliseconds 500
$ox = $r.Left + $Gutter
$oy = $r.Top + $Gutter
$cw = ($r.Right - $r.Left) - 2 * $Gutter
$ch = ($r.Bottom - $r.Top) - 2 * $Gutter

$bmp = New-Object System.Drawing.Bitmap($cw, $ch)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($ox, $oy, 0, 0, (New-Object System.Drawing.Size($cw, $ch)))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "$Out  ${cw}x${ch}"
