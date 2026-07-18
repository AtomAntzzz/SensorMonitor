@echo off
rem 新机器双击/命令行即用：以 Bypass 执行 setup.ps1，透传全部参数。
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0setup.ps1" %*
