# Click a list of screen points, in order. Each point is "x,y[,clicks]".
# SetCursorPos is placed AND VERIFIED: on a multi-monitor desktop a single set can land short.
# One string, points separated by ";" -- an array parameter arrives from a shell as a
# single joined string and every comma then reads as a field, which turned a click count
# of 1 into 222.
param([string]$Points, [int]$SettleMs = 900)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NClick {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out NPT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
}
public struct NPT { public int X, Y; }
"@
[NClick]::SetProcessDPIAware() | Out-Null

foreach ($pt in ($Points -split ';')) {
  if (-not $pt.Trim()) { continue }
  $a = $pt.Split(',')
  $x = [int]$a[0]; $y = [int]$a[1]
  $n = if ($a.Length -gt 2) { [int]$a[2] } else { 1 }

  for ($i = 0; $i -lt 8; $i++) {
    [NClick]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 60
    $p = New-Object 'NPT'
    [NClick]::GetCursorPos([ref]$p) | Out-Null
    if ($p.X -eq $x -and $p.Y -eq $y) { break }
  }
  for ($c = 0; $c -lt $n; $c++) {
    [NClick]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # LEFTDOWN
    [NClick]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # LEFTUP
    if ($n -gt 1) { Start-Sleep -Milliseconds 90 }
  }
  Write-Output "clicked $x,$y x$n"
  Start-Sleep -Milliseconds $SettleMs
}
