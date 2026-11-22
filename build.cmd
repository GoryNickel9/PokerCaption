@echo off
rem Build completo: DLL nativa x86/x64 + applicazione C#
setlocal
cd /d "%~dp0"

echo === DLL nativa (zig) ===
powershell -ExecutionPolicy Bypass -File native\build.ps1 || goto :err

echo === Applicazione C# ===
set "PC_DOTNET=dotnet.exe"
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "PC_DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
"%PC_DOTNET%" build src\PokerCaption\PokerCaption.csproj -c Release || goto :err

echo.
echo Build completata: src\PokerCaption\bin\Release\net8.0-windows\PokerCaption.exe
exit /b 0

:err
echo Build FALLITA
exit /b 1
