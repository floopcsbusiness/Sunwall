@echo off
setlocal
rem Builds Sunwall.exe with the C# compiler that ships with Windows.
rem No SDK, Visual Studio or NuGet package needed.

set "HERE=%~dp0"
set "SRC=%HERE%src"
set "OUT=%HERE%Sunwall.exe"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe under %WINDIR%\Microsoft.NET.
  echo Install the .NET Framework 4.x, or run this from a Developer Command Prompt.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /warn:4 ^
  /out:"%OUT%" ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%SRC%\*.cs"

if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo.
echo Built %OUT%
echo Run it with: Sunwall.exe
