@echo off
rem Baut die Anwendung nach dist\ (benoetigt .NET SDK 8 oder Visual Studio 2022)
cd /d "%~dp0"
dotnet publish src\Achsen.App\Achsen.App.vbproj -c Release -o dist
if errorlevel 1 (pause & exit /b 1)
echo.
echo Fertig: dist\Achsen.exe
pause
