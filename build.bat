@echo off
rem ============================================================
rem build.bat - 一键批处理编译（等同 SharpDevelop 的命令行构建）
rem
rem 用法: build.bat [Debug^|Release] [x64^|x86^|all]
rem   默认: build.bat Debug all
rem
rem 步骤:
rem   1. AsmLibrary.dll   MCF GCC g++ 编译 C 源（x64 + x86 双架构）
rem   2. TargetCpp.exe    MCF GCC g++ 编译 C++ 源
rem   3. AsmStudio.exe    内置 MSBuild 编译 C# 工程
rem   4. TargetSharp.exe  内置 MSBuild 编译 C# 工程
rem 清除请用 clean.bat
rem ============================================================
setlocal
setlocal enabledelayedexpansion

set CFG=%~1
if "%CFG%"=="" set CFG=Debug
set PLAT=%~2
if "%PLAT%"=="" set PLAT=all
if /i not "%CFG%"=="Debug" if /i not "%CFG%"=="Release" goto :usage
if /i not "%PLAT%"=="x64" if /i not "%PLAT%"=="x86" if /i not "%PLAT%"=="all" goto :usage

rem ---- 工具链 ----
set MSBUILD=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
set GCC64=E:\var\bin\mcf\x64\bin\gcc.exe
set GCC32=E:\var\bin\mcf\x32\bin\gcc.exe
if not exist "%MSBUILD%" set MSBUILD=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%MSBUILD%" (
    echo [!] 未找到内置 MSBuild
    goto :fail
)
if not exist "%GCC64%" (
    echo [!] 未找到 MCF GCC x64: %GCC64%
    goto :fail
)

rem ---- 源与参数 ----
rem C 库用 gcc 驱动（.c 默认按 C 编译，避免 g++ 驱动的 C++ name mangling）
set LIBSRC=AsmLibrary\src\core\dllmain.c AsmLibrary\src\exec\exec.c AsmLibrary\src\exec\trampoline.c AsmLibrary\src\pipe\pipe.c AsmLibrary\src\symbols\symbols.c AsmLibrary\src\util\util.c AsmLibrary\src\veh\veh.c
set LIBOPT=-std=c99 -O2 -Wall -Wno-unused-function -DASMHOST_BUILD -IAsmLibrary\include -IAsmLibrary\src
set CPPOPT=-std=c++11 -O2 -Wall -static

set FAILED=0

rem ---- 1/2. AsmLibrary.dll + TargetCpp.exe（g++，按架构）----
if /i "%PLAT%"=="x64" call :build_cpp x64
if /i "%PLAT%"=="x86" call :build_cpp x86
if /i "%PLAT%"=="all" (
    call :build_cpp x64
    call :build_cpp x86
)

rem ---- 3. AsmStudio.exe（C#，输出到 x64 目录）----
echo.
echo ===== [3/4] AsmStudio (C# / MSBuild %CFG%) =====
set | findstr /i "plat platform config" 
if not exist obj\%CFG%\AsmStudio mkdir obj\%CFG%\AsmStudio
"%MSBUILD%" AsmStudio\AsmStudio.csproj /nologo /m /v:m /p:Configuration=%CFG% "/p:OutputPath=..\bin\%CFG%\x64\"
if errorlevel 1 set FAILED=1

rem ---- 4. TargetSharp.exe（C#，输出到 x64 目录）----
echo.
echo ===== [4/4] TargetSharp (C# / MSBuild %CFG%) =====
if not exist obj\%CFG%\TargetSharp mkdir obj\%CFG%\TargetSharp
"%MSBUILD%" TargetSharp\TargetSharp.csproj /nologo /m /v:m /p:Configuration=%CFG% "/p:OutputPath=..\bin\%CFG%\x64\"
if errorlevel 1 set FAILED=1

echo.
if "%FAILED%"=="1" (
    echo ===== 构建完成，但存在失败项 =====
    exit /b 1
)
echo ===== 全部构建成功 [%CFG%] =====
exit /b 0

:build_cpp
set ARCH=%~1
if "%ARCH%"=="x64" (set GCC=%GCC64%) else (set GCC=%GCC32%)
if "%ARCH%"=="x86" (set MARCH=-m32) else (set MARCH=)
set OUTDIR=bin\%CFG%\%ARCH%
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if not exist obj\%CFG%\AsmLibrary\%ARCH% mkdir obj\%CFG%\AsmLibrary\%ARCH%
echo.
echo ===== [1/4] AsmLibrary.dll (%CFG% %ARCH%, g++) =====
"%GCC%" %MARCH% %LIBOPT% -shared -o "%OUTDIR%\AsmLibrary.dll" %LIBSRC%
if errorlevel 1 (
    set FAILED=1
    goto :eof
)
echo ===== [2/4] TargetCpp.exe (%CFG% %ARCH%, g++) =====
"%GCC%" %MARCH% %CPPOPT% -o "%OUTDIR%\TargetCpp.exe" TargetCpp\main.cpp
if errorlevel 1 (
    set FAILED=1
    goto :eof
)
goto :eof

:usage
echo 用法: build.bat [Debug^|Release] [x64^|x86^|all]
exit /b 2
