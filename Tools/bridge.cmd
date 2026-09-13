@echo off
REM ============================================================
REM  bridge.cmd  —  Unity Codely Bridge 客户端启动器
REM  用法:  bridge.cmd doctor
REM         bridge.cmd scene_info
REM         bridge.cmd read_console 50
REM         bridge.cmd execute_csharp "return Application.unityVersion;"
REM         bridge.cmd play / pause / stop
REM         bridge.cmd screenshot screenshots\a.png
REM
REM  作用: 自动找一个可用的 Python 解释器，再跑 unity_bridge.py。
REM        （本机没有独立安装 Python，所以这里做了多路探测）
REM ============================================================
setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

set "HERE=%~dp0"

REM --- 1) 显式覆盖优先级最高 ---
if defined UNITY_BRIDGE_PYTHON (
    if exist "%UNITY_BRIDGE_PYTHON%" (
        set "PYEXE=%UNITY_BRIDGE_PYTHON%"
        goto :run
    )
)

REM --- 2) 官方启动器 py.exe（真的装了 Python 时可用）---
for /f "delims=" %%i in ('where py 2^>nul') do (
    py -3 -c "import sys" >nul 2>&1
    if !errorlevel! equ 0 (
        set "PYEXE=py -3"
        goto :run
    )
)

REM --- 3) PATH 里的 python（要排除微软商店占位程序）---
for /f "delims=" %%i in ('where python 2^>nul') do (
    echo %%i | findstr /i "WindowsApps" >nul
    if !errorlevel! neq 0 (
        set "PYEXE=%%i"
        goto :run
    )
)

REM --- 4) 常见安装位置兜底 ---
for %%d in (
    "%LOCALAPPDATA%\Programs\Python"
    "%ProgramFiles%\Python"
    "C:\Python312"
    "C:\Python311"
) do (
    if exist "%%~d" (
        for /d %%v in ("%%~d\Python3*") do (
            if exist "%%~v\python.exe" (
                set "PYEXE=%%~v\python.exe"
                goto :run
            )
        )
    )
)

REM --- 5) 最后兜底: WorkBuddy 自带的隔离运行时 ---
for /d %%v in ("%USERPROFILE%\.workbuddy\binaries\python\versions\*") do (
    if exist "%%~v\python.exe" set "PYEXE=%%~v\python.exe"
)
if defined PYEXE goto :run

echo [ERROR] 找不到可用的 Python 3.7+ 解释器。
echo         请安装 Python 3.9+ : https://www.python.org/downloads/
echo         或设置环境变量 UNITY_BRIDGE_PYTHON 指向 python.exe
exit /b 1

:run
%PYEXE% "%HERE%unity_bridge.py" %*
exit /b %errorlevel%
