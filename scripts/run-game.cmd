@echo off
rem Double-click to build and start the simulation; arguments are passed on (e.g. -Editor).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-game.ps1" %*
if errorlevel 1 pause
