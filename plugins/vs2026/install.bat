@echo off
setlocal
title VsCopilotProxy Visual Studio 插件安装

cd /d "%~dp0"

echo ============================================
echo   VsCopilotProxy Visual Studio 插件 一键安装
echo ============================================
echo.

rem ── 1. 定位 VSIXInstaller.exe（自动扫描 VS 2022/2026 各版本目录）──
set "VSI="
for %%d in (C D E) do (
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
    echo [错误] 未找到 VSIXInstaller.exe，请确认已安装 Visual Studio。
    goto :end
)
echo [1/3] 找到安装器: %VSI%

rem ── 2. 选取 vsix：优先使用最新构建输出，其次用本目录自带副本 ──
set "VSIX="
for /f "delims=" %%f in ('dir /b /o-d "%~dp0..\..\Vs2026ChatProxy.VSIX\bin\Release\*.vsix" 2^>nul') do (
    if not defined VSIX set "VSIX=%~dp0..\..\Vs2026ChatProxy.VSIX\bin\Release\%%f"
)
if not defined VSIX for /f "delims=" %%f in ('dir /b /o-d "%~dp0*.vsix" 2^>nul') do (
    if not defined VSIX set "VSIX=%~dp0%%f"
)
if not defined VSIX (
    echo [错误] 未找到 .vsix 包。请先编译解决方案生成 Release，或把 vsix 放到本目录。
    goto :end
)
echo [2/3] 安装包: %VSIX%

rem ── 3. 检查 VS 是否正在运行（安装/卸载要求关闭 VS）──
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
