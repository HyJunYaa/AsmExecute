@echo off
rem ============================================================
rem build.bat - 一键批处理编译（纯 C#，无任何 C/C++ 工具链依赖）
rem
rem 用法: build.bat [Debug^|Release] [x64^|x86^|all]
rem   默认: build.bat Debug all
rem
rem 步骤:
rem   1. AsmStudio.exe   内置 MSBuild 编译 C# 工程（/v:n 显示完整过程）
rem   2. GameSharp.exe   内置 MSBuild 编译 C# 工程
rem 清除请用 clean.bat
rem
rem 注意: 所有变量名带 AS_ 前缀 —— MSBuild 会把环境变量映射为同名属性，
rem       普通名字（如 CFG/ARCH）会污染 csproj 的条件求值（实测教训）。
rem ============================================================
setlocal enabledelayedexpansion

set AS_CFG=%~1
if "%AS_CFG%"=="" set AS_CFG=Debug
set AS_PLAT=%~2
if "%AS_PLAT%"=="" set AS_PLAT=all
if /i not "%AS_CFG%"=="Debug" if /i not "%AS_CFG%"=="Release" goto :usage
if /i not "%AS_PLAT%"=="x64" if /i not "%AS_PLAT%"=="x86" if /i not "%AS_PLAT%"=="all" goto :usage

rem ---- 工具链（仅内置 MSBuild）----
set AS_MSBUILD=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
if not exist "%AS_MSBUILD%" set AS_MSBUILD=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%AS_MSBUILD%" (
    echo [!] 未找到内置 MSBuild
    exit /b 1
)

set AS_FAILED=0

rem ---- 1. AsmStudio.exe（C#，x64 + x86 双架构输出）----
echo ===== [1/2] AsmStudio (C# / MSBuild %AS_CFG%) =====
if not exist obj\%AS_CFG%\AsmStudio mkdir obj\%AS_CFG%\AsmStudio
if /i "%AS_PLAT%"=="x64" call :build_cs AsmStudio AsmStudio.csproj x64
if /i "%AS_PLAT%"=="x86" call :build_cs AsmStudio AsmStudio.csproj x86
if /i "%AS_PLAT%"=="all" (
    call :build_cs AsmStudio AsmStudio.csproj x64
    call :build_cs AsmStudio AsmStudio.csproj x86
)
if "%AS_FAILED%"=="1" goto :done

rem ---- 2. GameSharp.exe（C#，x64 + x86 双架构输出）----
echo.
echo ===== [2/2] GameSharp (C# / MSBuild %AS_CFG%) =====
if not exist obj\%AS_CFG%\GameSharp mkdir obj\%AS_CFG%\GameSharp
if /i "%AS_PLAT%"=="x64" call :build_cs GameSharp GameSharp.csproj x64
if /i "%AS_PLAT%"=="x86" call :build_cs GameSharp GameSharp.csproj x86
if /i "%AS_PLAT%"=="all" (
    call :build_cs GameSharp GameSharp.csproj x64
    call :build_cs GameSharp GameSharp.csproj x86
)

:done
echo.
if "%AS_FAILED%"=="1" (
    echo ===== 构建完成，但存在失败项 =====
    exit /b 1
)
echo ===== 全部构建成功 [%AS_CFG%] =====
exit /b 0

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
if errorlevel 1 set AS_FAILED=1
exit /b 0

:usage
echo 用法: build.bat [Debug^|Release] [x64^|x86^|all]
exit /b 2
