@echo off
setlocal
title Vs2026CopilotProxy Visual Studio 插件安装

cd /d "%~dp0"

echo ============================================
echo   Vs2026CopilotProxy Visual Studio 插件 一键安装
echo ============================================
echo.

rem ── remote 参数：跳过本地源，强制从 GitHub 下载最新 Release ──
set "FORCE_REMOTE="
if /i "%~1"=="remote" set "FORCE_REMOTE=1"
if defined FORCE_REMOTE echo 模式: 强制网络安装（跳过本地构建与副本）

rem ── 1. 定位 VSIXInstaller.exe（优先 vswhere 官方定位，支持任意安装目录；回退扫描默认目录）──
set "VSI="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
    for /f "usebackq delims=" %%p in (`"%VSWHERE%" -latest -prerelease -products * -find Common7\IDE\VSIXInstaller.exe 2^>nul`) do (
        if not defined VSI set "VSI=%%p"
    )
)
if not defined VSI for %%d in (C D E) do (
    if exist "%%d:\Program Files\Microsoft Visual Studio" (
        for /f "delims=" %%v in ('dir /b /ad "%%d:\Program Files\Microsoft Visual Studio" 2^>nul') do (
            for /f "delims=" %%e in ('dir /b /ad "%%d:\Program Files\Microsoft Visual Studio\%%v" 2^>nul') do (
                if exist "%%d:\Program Files\Microsoft Visual Studio\%%v\%%e\Common7\IDE\VSIXInstaller.exe" (
                    set "VSI=%%d:\Program Files\Microsoft Visual Studio\%%v\%%e\Common7\IDE\VSIXInstaller.exe"
                )
            )
        )
    )
)
if not defined VSI (
    echo [错误] 未找到 VSIXInstaller.exe（vswhere 与默认目录扫描均未命中）。
    echo        若 VS 安装在自定义路径，请手动执行:
    echo        "VS安装目录\Common7\IDE\VSIXInstaller.exe" 插件.vsix
    goto :end
)
echo [1/3] 找到安装器: %VSI%

rem ── 2. 选取 vsix。优先级: 本地构建输出 ^> GitHub 最新 Release ^> 本目录副本 ──
set "VSIX="
set "SRC="
if not defined FORCE_REMOTE (
    for /f "delims=" %%f in ('dir /b /o-d "%~dp0..\..\Vs2026ChatProxy.VSIX\bin\Release\*.vsix" 2^>nul') do (
        if not defined VSIX set "VSIX=%~dp0..\..\Vs2026ChatProxy.VSIX\bin\Release\%%f"
    )
    if defined VSIX set "SRC=本地构建输出" & goto :got_vsix
)

echo 正在从 GitHub 下载最新 Release ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ok=$false; foreach($i in 1..3){ try{ Invoke-WebRequest 'https://github.com/zxxfhh/Vs2026ChatProxy/releases/latest/download/Vs2026CopilotProxy.vsix' -OutFile (Join-Path $env:TEMP 'Vs2026CopilotProxy-latest.vsix'); $ok=$true; break }catch{ Start-Sleep 2 } }; if($ok){exit 0}else{exit 1}"
if not errorlevel 1 (
    if exist "%TEMP%\Vs2026CopilotProxy-latest.vsix" (
        set "VSIX=%TEMP%\Vs2026CopilotProxy-latest.vsix"
        set "SRC=GitHub Release 最新版"
    )
)
if defined VSIX goto :got_vsix

if not defined FORCE_REMOTE (
    for /f "delims=" %%f in ('dir /b /o-d "%~dp0*.vsix" 2^>nul') do (
        if not defined VSIX set "VSIX=%~dp0%%f"
    )
    if defined VSIX set "SRC=本目录自带副本" & goto :got_vsix
)

echo [错误] 未找到 .vsix 包（本地无构建输出、GitHub 下载失败、本目录无副本）。
echo        若仓库为私有，Release 下载需要登录权限，请先公开仓库或使用本地构建。
goto :end

:got_vsix
echo [2/3] 安装包: %VSIX%
echo       来源:   %SRC%

rem ── 3. 检查 VS 是否正在运行（安装要求关闭 VS）──
tasklist /fi "imagename eq devenv.exe" 2>nul | find /i "devenv.exe" >nul
if not errorlevel 1 (
    echo [警告] 检测到 Visual Studio 正在运行，建议先全部关闭再安装。
    choice /c YN /m "仍然继续 [Y=继续 N=退出]"
    if errorlevel 2 goto :end
)

echo [3/3] 启动 VSIX 安装器（请在弹出的窗口中选择实例并确认）...
"%VSI%" "%VSIX%"

echo.
echo [OK] 安装流程已执行。启动 VS 后通过菜单
echo      View ^> Other Windows ^> VsCopilotProxy 打开工具窗口。

:end
echo.
pause
