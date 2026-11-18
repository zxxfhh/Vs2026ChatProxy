@echo off
setlocal
title VsCopilotProxy Visual Studio 插件卸载

echo ============================================
echo   VsCopilotProxy Visual Studio 插件 卸载
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
echo [1/2] 找到安装器: %VSI%

rem ── 2. 检查 VS 是否正在运行（卸载要求关闭 VS）──
tasklist /fi "imagename eq devenv.exe" 2>nul | find /i "devenv.exe" >nul
if not errorlevel 1 (
    echo [警告] 检测到 Visual Studio 正在运行，请先全部关闭再卸载。
    choice /c YN /m "仍然继续 [Y=继续 N=退出]"
    if errorlevel 2 goto :end
)

echo [2/2] 正在卸载 VsCopilotProxy ...
"%VSI%" /uninstall:VsCopilotProxy.c3d4e5f6-a7b8-9012-cdef-123456789012

echo.
echo [OK] 卸载流程已执行。若提示成功，重启 VS 生效。
echo      若卸载仍然失败，先关闭全部 VS 窗口后再运行本脚本。

:end
echo.
pause
