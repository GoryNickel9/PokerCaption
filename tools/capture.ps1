param(
  [string]$out = "shot.png",
  [int]$procId = 0,
  [string]$procName = ""
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = $null
if ($procId -ne 0) { $p = Get-Process -Id $procId -ErrorAction SilentlyContinue }
elseif ($procName) { $p = Get-Process -Name $procName -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($null -eq $p) { Write-Output "processo non trovato"; exit 1 }

$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "nessuna finestra principale"; exit 1 }

[void][W]::ShowWindow($h, 9)
Start-Sleep -Milliseconds 500

$r = New-Object W+RECT
[void][W]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L
$hh = $r.B - $r.T
if ($w -le 0 -or $hh -le 0) { Write-Output "rect non valido"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# PW_RENDERFULLCONTENT: cattura anche le finestre sovrapposte
$ok = [W]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) {
  # fallback: copia dallo schermo
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $hh))
}
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $out ($w x $hh) printwindow=$ok"
