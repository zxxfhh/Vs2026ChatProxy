@echo off
setlocal
title VsCodeCopilotProxy VS Code 插件一键安装

cd /d "%~dp0"

echo ============================================
echo   VsCodeCopilotProxy VS Code 插件 一键安装
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

rem ── 2. 选取 vsix。优先级: 源码目录最新打包 ^> GitHub 最新 Release ^> 本目录副本 ──
set "VSIX="
set "SRC="
for /f "delims=" %%f in ('dir /b /o-d "%~dp0..\..\vscode-extension\*.vsix" 2^>nul') do (
    if not defined VSIX set "VSIX=%~dp0..\..\vscode-extension\%%f"
)
if defined VSIX set "SRC=源码目录最新打包" & goto :got_vsix

echo 正在从 GitHub 下载最新 Release ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try{Invoke-WebRequest 'https://github.com/zxxfhh/Vs2026ChatProxy/releases/latest/download/VsCodeCopilotProxy.vsix' -OutFile (Join-Path $env:TEMP 'VsCodeCopilotProxy-latest.vsix');exit 0}catch{exit 1}"
if not errorlevel 1 (
    if exist "%TEMP%\VsCodeCopilotProxy-latest.vsix" (
        set "VSIX=%TEMP%\VsCodeCopilotProxy-latest.vsix"
        set "SRC=GitHub Release 最新版"
    )
)
if defined VSIX goto :got_vsix

for /f "delims=" %%f in ('dir /b /o-d "%~dp0*.vsix" 2^>nul') do (
    if not defined VSIX set "VSIX=%~dp0%%f"
)
if defined VSIX set "SRC=本目录自带副本" & goto :got_vsix

echo [错误] 未找到 .vsix 包（源码目录无打包、GitHub 下载失败、本目录无副本）。
echo        若仓库为私有，Release 下载需要登录权限，请先公开仓库或使用本地构建。
goto :end

:got_vsix
echo [2/3] 安装包: %VSIX%
echo       来源:   %SRC%

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
