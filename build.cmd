@echo off
REM Switch console to UTF-8 so dotnet's Chinese output is not garbled (OEM codepage 936).
chcp 65001 >nul
REM Bypass PowerShell execution policy (default Restricted blocks .ps1 files).
REM Usage: build.cmd           -> compile
REM        build.cmd -Deploy   -> compile + deploy
REM NOTE: keep this file ASCII-only. cmd.exe reads it in the OEM codepage.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
