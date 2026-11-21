@echo off
rem ============================================================
rem clean.bat - 清除全部构建产物（等同 SharpDevelop 的 Clean）
rem   删除 bin\ 与 obj\ 两个输出树（不改任何源码/工程文件）
rem ============================================================
setlocal

if exist bin (
    echo 清除 bin\ ...
    rmdir /s /q bin
)
if exist obj (
    echo 清除 obj\ ...
    rmdir /s /q obj
)

if exist bin (
    echo [!] bin\ 清除失败（可能有文件被占用，请关闭正在运行的程序后重试）
    exit /b 1
)
if exist obj (
    echo [!] obj\ 清除失败
    exit /b 1
)
echo ===== 清除完成 =====
exit /b 0
