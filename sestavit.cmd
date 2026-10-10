@echo off
rem Sestavi ObrysCNC.exe pomoci prekladace, ktery je soucasti Windows (.NET Framework 4.x).
setlocal
set "NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%NET%\csc.exe" set "NET=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%NET%\csc.exe"
if not exist "%CSC%" (
  echo Prekladac csc.exe nebyl nalezen. Je nainstalovan .NET Framework 4?
  exit /b 1
)
cd /d "%~dp0"
set "OUTPUT=ObrysCNC.exe"
if not "%~1"=="" set "OUTPUT=%~1"
"%CSC%" /nologo /codepage:65001 /target:winexe /langversion:5 /optimize+ /platform:anycpu /out:"%OUTPUT%" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll ^
  /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\WindowsBase.dll" ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Sestaveni selhalo.
  exit /b 1
)
echo Hotovo: %~dp0%OUTPUT%
