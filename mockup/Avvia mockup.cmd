@echo off
setlocal
set "MOCKUP_DLL=%~dp0Desktop\PokerCaption.Mockup.dll"
set "MOCKUP_RUNTIME=%USERPROFILE%\dotnet-x86\dotnet.exe"
if exist "%MOCKUP_RUNTIME%" (
    "%MOCKUP_RUNTIME%" "%MOCKUP_DLL%" --form Form1 1
) else (
    "%~dp0Desktop\PokerCaption.Mockup.exe" --form Form1 1
)
if errorlevel 1 pause
