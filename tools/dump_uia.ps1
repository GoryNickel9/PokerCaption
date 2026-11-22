# Dump albero UIA delle finestre del client Sisal (ricerca nickname pre-tavolo).
# UIA nativo disponibile solo in PowerShell (.NET Framework): usa QUESTO script.
# Uso: powershell -ExecutionPolicy Bypass -File dump_uia.ps1 [-out uia_dump.txt]
# NOTA: salta le finestre GLFW30 (Flutter blocca le query UIA).
param([string]$out = "uia_dump.txt")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WU {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr OpenProcess(uint a, bool i, uint p);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("psapi.dll", CharSet = CharSet.Unicode)] public static extern bool GetModuleFileNameEx(IntPtr h, IntPtr m, StringBuilder s, uint n);
}
"@

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("=== UIA tree dei processi Sisal/PokerStars (senza GLFW30) ===")

$wins = New-Object System.Collections.ArrayList
$cb = {
  param($h, $l)
  if (-not [WU]::IsWindowVisible($h)) { return $true }
  $cls = New-Object System.Text.StringBuilder 128
  [void][WU]::GetClassNameW($h, $cls, 128)
  $c = $cls.ToString()
  if ($c -eq 'GLFW30') { return $true }   # Flutter: blocca le query
  $ttl = New-Object System.Text.StringBuilder 300
  [void][WU]::GetWindowTextW($h, $ttl, 300)
  $pid2 = 0
  [void][WU]::GetWindowThreadProcessId($h, [ref]$pid2)
  if ($pid2 -eq 0) { return $true }
  $exe = ''
  $hp = [WU]::OpenProcess(0x1000, $false, $pid2)
  if ($hp -ne [IntPtr]::Zero) {
    $psb = New-Object System.Text.StringBuilder 1024
    if ([WU]::GetModuleFileNameEx($hp, [IntPtr]::Zero, $psb, 1024)) { $exe = $psb.ToString() }
    [void][WU]::CloseHandle($hp)
  }
  if ($exe -match 'sisal|pokerstars|betfair') {
    [void]$wins.Add(@{ H = $h; Cls = $c; Title = $ttl.ToString(); Pid = $pid2; Exe = $exe })
  }
  return $true
}
[System.Windows.Automation.AutomationElement]::RootElement # inizializza UIA
# EnumWindows via AutomationElement raiz: usiamo Get-Process + Win32 sopra
$sig = $cb
Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;
public class EW { public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P cb, IntPtr l); }
"@
$enumCb = [EW+P]$cb
[void][EW]::EnumWindows($enumCb, [IntPtr]::Zero)

foreach ($w in $wins) {
  [void]$sb.AppendLine("0x$('{0:X}' -f $w.H.ToInt64()) [$($w.Cls)] '$($w.Title)' pid=$($w.Pid) $([System.IO.Path]::GetFileName($w.Exe))")
  try {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($w.H)
    if ($null -eq $root) { [void]$sb.AppendLine('  (UIA n/d)'); continue }
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    [void]$sb.AppendLine("  elementi: $($all.Count)")
    $i = 0
    foreach ($e in $all) {
      $nm = $e.Current.Name
      if ($nm) { [void]$sb.AppendLine("    [$($e.Current.ControlType.ProgrammaticName)] '$nm'") }
      $i++; if ($i -gt 300) { [void]$sb.AppendLine('    ...troncato'); break }
    }
  } catch { [void]$sb.AppendLine('  UIA ERR: ' + $_.Exception.Message) }
  [void]$sb.AppendLine('')
}
Set-Content -Path $out -Value $sb.ToString() -Encoding UTF8
Write-Output "scritto $out"
