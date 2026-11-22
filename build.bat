@echo off
rem ============================================================
rem build.bat - 一键批处理编译（等同 SharpDevelop 的命令行构建）
rem
rem 用法: build.bat [Debug^|Release] [x64^|x86^|all]
rem   默认: build.bat Debug all
rem
rem 步骤:
rem   1. AsmLibrary.dll   MCF GCC gcc 逐文件编译 C 源 + 链接（x64/x86 双架构）
rem   2. TargetCpp.exe    MCF GCC g++ 编译 C++ 源
rem   3. AsmStudio.exe    内置 MSBuild 编译 C# 工程（/v:n 显示完整过程）
rem   4. TargetSharp.exe  内置 MSBuild 编译 C# 工程
rem 清除请用 clean.bat
rem
rem 注意: 所有变量名带 AS_ 前缀 —— MSBuild 会把环境变量映射为同名属性，
rem       普通名字（如 PLAT/ARCH）会污染 csproj 的条件求值（实测教训）。
rem ============================================================
setlocal enabledelayedexpansion

set AS_CFG=%~1
if "%AS_CFG%"=="" set AS_CFG=Debug
set AS_PLAT=%~2
if "%AS_PLAT%"=="" set AS_PLAT=all
if /i not "%AS_CFG%"=="Debug" if /i not "%AS_CFG%"=="Release" goto :usage
if /i not "%AS_PLAT%"=="x64" if /i not "%AS_PLAT%"=="x86" if /i not "%AS_PLAT%"=="all" goto :usage

rem ---- 工具链 ----
set AS_MSBUILD=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
set AS_GCC64=E:\var\bin\mcf\x64\bin\gcc.exe
set AS_GPP64=E:\var\bin\mcf\x64\bin\g++.exe
set AS_GCC32=E:\var\bin\mcf\x32\bin\gcc.exe
set AS_GPP32=E:\var\bin\mcf\x32\bin\g++.exe
if not exist "%AS_MSBUILD%" set AS_MSBUILD=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%AS_MSBUILD%" (
    echo [!] 未找到内置 MSBuild
    exit /b 1
)
if not exist "%AS_GCC64%" (
    echo [!] 未找到 MCF GCC x64: %AS_GCC64%
    exit /b 1
)

rem ---- 源与参数 ----
rem C 库用 gcc 驱动（.c 默认按 C 编译，避免 g++ 驱动的 C++ name mangling）
set AS_LIBSRC=AsmLibrary\src\core\dllmain.c AsmLibrary\src\exec\exec.c AsmLibrary\src\exec\trampoline.c AsmLibrary\src\pipe\pipe.c AsmLibrary\src\symbols\symbols.c AsmLibrary\src\util\util.c AsmLibrary\src\veh\veh.c
set AS_LIBOPT=-std=c99 -O2 -Wall -Wno-unused-function -DASMHOST_BUILD -IAsmLibrary\include -IAsmLibrary\src
set AS_CPPOPT=-std=c++11 -O2 -Wall -static

set AS_FAILED=0

rem ---- 1/2. AsmLibrary.dll + TargetCpp.exe（gcc/g++，按架构）----
if /i "%AS_PLAT%"=="x64" call :build_cpp x64
if /i "%AS_PLAT%"=="x86" call :build_cpp x86
if /i "%AS_PLAT%"=="all" (
    call :build_cpp x64
    call :build_cpp x86
)

rem ---- 3. AsmStudio.exe（C#，x64 + x86 双架构输出）----
echo.
echo ===== [3/4] AsmStudio (C# / MSBuild %AS_CFG%) =====
if not exist obj\%AS_CFG%\AsmStudio mkdir obj\%AS_CFG%\AsmStudio
call :build_cs AsmStudio AsmStudio.csproj x64
if errorlevel 1 set AS_FAILED=1
call :build_cs AsmStudio AsmStudio.csproj x86
if errorlevel 1 set AS_FAILED=1

rem ---- 4. TargetSharp.exe（C#，x64 + x86 双架构输出）----
echo.
echo ===== [4/4] TargetSharp (C# / MSBuild %AS_CFG%) =====
if not exist obj\%AS_CFG%\TargetSharp mkdir obj\%AS_CFG%\TargetSharp
call :build_cs TargetSharp TargetSharp.csproj x64
if errorlevel 1 set AS_FAILED=1
call :build_cs TargetSharp TargetSharp.csproj x86
if errorlevel 1 set AS_FAILED=1

echo.
if "%AS_FAILED%"=="1" (
    echo ===== 构建完成，但存在失败项 =====
    exit /b 1
)
echo ===== 全部构建成功 [%AS_CFG%] =====
exit /b 0

:build_cpp
set AS_ARCH=%~1
if /i "%AS_ARCH%"=="x64" (
    set AS_CC=%AS_GCC64%
    set AS_CXX=%AS_GPP64%
    set AS_MARCH=
) else (
    set AS_CC=%AS_GCC32%
    set AS_CXX=%AS_GPP32%
    set AS_MARCH=-m32
)
set AS_OUTDIR=bin\%AS_CFG%\%AS_ARCH%
set AS_OBJDIR=obj\%AS_CFG%\AsmLibrary\%AS_ARCH%
if not exist "%AS_OUTDIR%" mkdir "%AS_OUTDIR%"
if not exist "%AS_OBJDIR%" mkdir "%AS_OBJDIR%"

echo.
echo ===== [1/4] AsmLibrary.dll (%AS_CFG% %AS_ARCH%, gcc) =====
rem 逐文件编译（显示完整编译过程）
for %%F in (%AS_LIBSRC%) do (
    echo [CC] %%F
    call :compile_c %%F "%AS_OBJDIR%\%%~nF.o"
    if errorlevel 1 (
        set AS_FAILED=1
        goto :eof
    )
)
echo [LINK] AsmLibrary.dll
"%AS_CC%" %AS_MARCH% -shared -o "%AS_OUTDIR%\AsmLibrary.dll" "%AS_OBJDIR%\*.o"
if errorlevel 1 (
    set AS_FAILED=1
    goto :eof
)

echo.
echo ===== [2/4] TargetCpp.exe (%AS_CFG% %AS_ARCH%, g++) =====
echo [CXX] TargetCpp\main.cpp
"%AS_CXX%" %AS_MARCH% %AS_CPPOPT% -o "%AS_OUTDIR%\TargetCpp.exe" TargetCpp\main.cpp
if errorlevel 1 (
    set AS_FAILED=1
    goto :eof
)
goto :eof

:compile_c
rem %1=源文件 %2=目标 .o
"%AS_CC%" %AS_MARCH% %AS_LIBOPT% -c "%~1" -o "%~2"
exit /b %errorlevel%

:build_cs
rem %1=工程目录 %2=csproj 文件名 %3=架构
rem   x64 → Platform=AnyCPU（csproj 默认）+ 输出 ..\bin\%CFG%\x64\
rem   x86 → Platform=x86（csproj 的 Debug|x86 组，PlatformTarget=x86 32 位优先）+ 输出 ..\bin\%CFG%\x86\
set AS_PROJDIR=%~1
set AS_CSPROJ=%~2
set AS_CSARCH=%~3
echo --- [%AS_CSARCH%] %AS_PROJDIR%\%AS_CSPROJ% ---
if /i "%AS_CSARCH%"=="x86" (
    "%AS_MSBUILD%" "%AS_PROJDIR%\%AS_CSPROJ%" /nologo /m /v:n /p:Configuration=%AS_CFG% /p:Platform=x86 "/p:OutputPath=..\bin\%AS_CFG%\x86"
) else (
    "%AS_MSBUILD%" "%AS_PROJDIR%\%AS_CSPROJ%" /nologo /m /v:n /p:Configuration=%AS_CFG% /p:Platform=AnyCPU "/p:OutputPath=..\bin\%AS_CFG%\x64"
)
if errorlevel 1 exit /b 1
exit /b 0

:usage
echo 用法: build.bat [Debug^|Release] [x64^|x86^|all]
exit /b 2

