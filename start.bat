@echo off
cd /d "%~dp0"
python -m catia_achsen
if errorlevel 1 pause
