@echo off
setlocal
title VsChatProxy VS Code 插件卸载

echo ============================================
echo   VsChatProxy VS Code 插件 卸载
echo ============================================
echo.

rem ── 1. 定位 VS Code 的 code CLI（必须取完整路径，
rem      否则 code.cmd 内部的 %~dp0 会退化为当前目录）──
set "CODE_CMD="
for /f "delims=" %%i in ('where code 2^>nul') do (
    if not defined CODE_CMD if /i "%%~xi"==".cmd" set "CODE_CMD=%%~fi"
)
if not defined CODE_CMD if exist "D:\Program Files\Microsoft VS Code\bin\code.cmd" set "CODE_CMD=D:\Program Files\Microsoft VS Code\bin\code.cmd"
if not defined CODE_CMD if exist "%ProgramFiles%\Microsoft VS Code\bin\code.cmd" set "CODE_CMD=%ProgramFiles%\Microsoft VS Code\bin\code.cmd"
if not defined CODE_CMD if exist "%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd" set "CODE_CMD=%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd"

if not defined CODE_CMD (
    echo [错误] 未找到 VS Code 的 code 命令。
    goto :end
)
echo [1/2] 找到 VS Code CLI: %CODE_CMD%

rem ── 2. 卸载（扩展 ID: 发布者.名称）──
echo [2/2] 正在卸载 zxxfhh.vs2026-chat-proxy ...
call "%CODE_CMD%" --uninstall-extension zxxfhh.vs2026-chat-proxy
if errorlevel 1 (
    echo.
    echo [提示] 卸载返回非零（可能本来就没安装）。
    goto :end
)

echo.
echo [OK] 卸载完成！若 VS Code 正在运行，请重启 VS Code 后生效。

:end
echo.
pause
