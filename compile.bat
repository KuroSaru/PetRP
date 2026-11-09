@echo off
title Compile PetRP-Companion.exe
cd /d "%~dp0"

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    echo [ERROR] Could not find .NET C# compiler at %CSC%
    pause
    exit /b 1
)

del /f /q "PetRP-Companion.old*.exe" >nul 2>&1
if exist "PetRP-Companion.exe" (
    move /y "PetRP-Companion.exe" "PetRP-Companion.old.%RANDOM%.exe" >nul 2>&1
)

set ICON_ARG=
if exist "app.ico" (
    set ICON_ARG=/win32icon:"app.ico"
)

echo Compiling PetRPCompanion.cs -> PetRP-Companion.exe ...
%CSC% /nologo /target:winexe %ICON_ARG% /out:"PetRP-Companion.exe" /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Net.Http.dll "PetRPCompanion.cs"

if %ERRORLEVEL% EQU 0 (
    del /f /q "PetRP-Companion.old*.exe" >nul 2>&1
    echo [SUCCESS] Built PetRP-Companion.exe with WoW Forever icon!
) else (
    echo [FAILED] Compilation failed with error code %ERRORLEVEL%.
)
if not "%1"=="--nopause" pause
