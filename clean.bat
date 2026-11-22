@echo off
rem ============================================================
rem clean.bat - 清除全部构建产物（等同 SharpDevelop 的 Clean）
rem   直接删除 bin\ 与 obj\ 产物文件夹，并清理各工程目录下的残留
rem ============================================================
setlocal

echo 清除构建产物文件夹...

rem ---- 根目录产物树 ----
if exist "%~dp0bin"  rd /s /q "%~dp0bin"
if exist "%~dp0obj"  rd /s /q "%~dp0obj"

rem ---- 各工程目录下的残留（历史构建可能落在这里）----
if exist "%~dp0AsmStudio\bin"    rd /s /q "%~dp0AsmStudio\bin"
if exist "%~dp0AsmStudio\obj"    rd /s /q "%~dp0AsmStudio\obj"
if exist "%~dp0GameSharp\bin"    rd /s /q "%~dp0GameSharp\bin"
if exist "%~dp0GameSharp\obj"    rd /s /q "%~dp0GameSharp\obj"

rem ---- 校验 ----
set AS_LEFT=0
if exist "%~dp0bin"  set AS_LEFT=1
if exist "%~dp0obj"  set AS_LEFT=1
if exist "%~dp0AsmStudio\bin"   set AS_LEFT=1
if exist "%~dp0GameSharp\bin"   set AS_LEFT=1

if "%AS_LEFT%"=="1" (
    echo [!] 部分文件夹未能删除（可能被占用，请关闭正在运行的程序后重试）
    timeout /t 3
    exit /b 1
)
echo ===== 清除完成 =====
timeout /t 3
exit /b 0
