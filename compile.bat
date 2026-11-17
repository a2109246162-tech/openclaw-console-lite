@echo off
rem OpenClaw Console Lite - non-interactive compile (ASCII only)
cd /d "%~dp0"
echo === Compiling LiteConsole.exe ===
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -codepage:65001 -out:build\LiteConsole.exe -win32manifest:src\app.manifest -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.Management.dll -r:System.Core.dll src\Core.cs src\UiBase.cs src\UiForms.cs > build\compile.log 2>&1
if errorlevel 1 (
  echo [FAIL] compile error
  type build\compile.log
  exit /b 1
)
echo [OK] compile succeeded
echo === Self test (isolated temp profile, real data untouched) ===
build\LiteConsole.exe --selftest
timeout /t 2 >nul
type "%TEMP%\lite-selftest.txt"
echo.
echo [DONE] build\LiteConsole.exe ready.
