# Build della DLL nativa captionHook per x86 e x64 (zig cc — mingw-w64)
# Uso: powershell -File native\build.ps1
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "captionHook.c"
$out = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force "$out\x86", "$out\x64" | Out-Null

$env:ZIG_LOCAL_CACHE_DIR = Join-Path $PSScriptRoot ".zig-cache"
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $PSScriptRoot ".zig-cache"

# .def distinti: su x86 il simbolo stdcall è decorato (hookProc@12) e va
# riesportato col nome pulito; su x64 non esiste decorazione.
$minhook = Join-Path $PSScriptRoot "third_party\minhook"
$hookSources = @("src\buffer.c", "src\hook.c", "src\trampoline.c", "src\hde\hde32.c") |
    ForEach-Object { Join-Path $minhook $_ }
zig cc -shared -O2 -Wall -target x86-windows-gnu -I "$minhook\include" -o "$out\x86\captionHook.dll" $src @hookSources (Join-Path $PSScriptRoot "captionHook_x86.def")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
zig cc -shared -O2 -Wall -target x86_64-windows-gnu -o "$out\x64\captionHook.dll" $src (Join-Path $PSScriptRoot "captionHook_x64.def")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "built:"
Get-ChildItem $out -Recurse -Filter *.dll | ForEach-Object { "  $($_.FullName) ($($_.Length) bytes)" }
