@echo off
rem OpenClaw Console Lite - build script (ASCII only)
cd /d "%~dp0"
echo === Compiling LiteConsole.exe ===
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -out:build\LiteConsole.exe -win32manifest:src\app.manifest -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.Management.dll -r:System.Core.dll src\LiteConsole.cs
if errorlevel 1 (
  echo.
  echo [FAIL] compile error
  pause
  exit /b 1
)
echo === Self test (isolated temp profile, real data untouched) ===
build\LiteConsole.exe --selftest
timeout /t 2 >nul
type "%TEMP%\lite-selftest.txt"
echo.
echo [DONE] build\LiteConsole.exe ready. SELFTEST=PASS means OK.
pause
