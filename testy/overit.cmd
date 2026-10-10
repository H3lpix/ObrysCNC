@echo off
setlocal
cd /d "%~dp0.."
call sestavit.cmd
if errorlevel 1 exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist testy\vysledky mkdir testy\vysledky
"%CSC%" /nologo /codepage:65001 /langversion:5 /out:testy\vysledky\TestGeom.exe /r:ObrysCNC.exe /r:System.Drawing.dll /r:System.Core.dll testy\TestGeom.cs
if errorlevel 1 exit /b 1
copy /y ObrysCNC.exe testy\vysledky\ObrysCNC.exe >nul
testy\vysledky\TestGeom.exe testy\vysledky testy\motivy.png testy\logo_lisplast.png testy\strom.webp > testy\vysledky\TestGeom.txt
type testy\vysledky\TestGeom.txt
findstr /c:"VSECHNY KONTROLY PROSLY" testy\vysledky\TestGeom.txt >nul
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /langversion:5 /out:testy\vysledky\TestFunkce.exe /r:ObrysCNC.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll testy\TestFunkce.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /langversion:5 /out:testy\vysledky\TestZamek.exe /r:ObrysCNC.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll testy\TestZamek.cs
if errorlevel 1 exit /b 1
testy\vysledky\TestFunkce.exe testy\vysledky > testy\vysledky\TestFunkce.txt
if errorlevel 1 (
  type testy\vysledky\TestFunkce.txt
  exit /b 1
)
type testy\vysledky\TestFunkce.txt
testy\vysledky\TestZamek.exe > testy\vysledky\TestZamek.txt
if errorlevel 1 exit /b 1
type testy\vysledky\TestZamek.txt
"%CSC%" /nologo /codepage:65001 /langversion:5 /out:testy\vysledky\TestSouradniceStroje.exe /r:ObrysCNC.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll testy\TestSouradniceStroje.cs
if errorlevel 1 exit /b 1
testy\vysledky\TestSouradniceStroje.exe testy\vysledky > testy\vysledky\TestSouradniceStroje.txt
if errorlevel 1 (
  type testy\vysledky\TestSouradniceStroje.txt
  exit /b 1
)
type testy\vysledky\TestSouradniceStroje.txt
"%CSC%" /nologo /codepage:65001 /langversion:5 /out:testy\vysledky\TestRadiusVyberu.exe /r:ObrysCNC.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll testy\TestRadiusVyberu.cs
if errorlevel 1 exit /b 1
testy\vysledky\TestRadiusVyberu.exe testy\vysledky\editor-radius.png > testy\vysledky\TestRadiusVyberu.txt
if errorlevel 1 exit /b 1
type testy\vysledky\TestRadiusVyberu.txt
exit /b 0
