@echo off
setlocal
title VsChatProxy VS Code 插件一键安装

rem ── 切到本脚本所在目录 ──
cd /d "%~dp0"

echo ============================================
echo   VsChatProxy VS Code 插件 一键安装
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
    echo        请确认已安装 VS Code，或将其 bin 目录加入 PATH 后重试。
    goto :end
)
echo [1/3] 找到 VS Code CLI: %CODE_CMD%

rem ── 2. 取本目录最新的 .vsix（按修改时间，避免写死版本号）──
set "VSIX="
for /f "delims=" %%f in ('dir /b /o-d "*.vsix" 2^>nul') do (
    if not defined VSIX set "VSIX=%%f"
)
if not defined VSIX (
    echo [错误] 本目录没有 .vsix 包。
    echo        请先在本目录执行:  npm install ^&^& npm run compile ^&^& npx vsce package
    goto :end
)
echo [2/3] 安装包: %VSIX%

rem ── 3. 安装（--force 允许覆盖同版本）──
echo [3/3] 正在安装...
call "%CODE_CMD%" --install-extension "%VSIX%" --force
if errorlevel 1 (
    echo.
    echo [失败] 安装出错，请把上方完整输出截图反馈。
    goto :end
)

echo.
echo 已安装版本:
call "%CODE_CMD%" --list-extensions --show-versions 2>nul | findstr /i "vs2026-chat-proxy"
echo.
echo [OK] 安装完成！若 VS Code 正在运行，请重启 VS Code 后生效。
echo      下一步: 命令面板执行  VsChatProxy: 配置 VS Code 聊天模型

:end
echo.
pause
