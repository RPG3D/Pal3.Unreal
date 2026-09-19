@echo off
setlocal enabledelayedexpansion

rem ============================================================================
rem BuildAndroid.bat - One-shot Android packaging (MobileClr reverse flow)
rem
rem Usage:
rem   BuildAndroid.bat [Config] [--skip-editor] [--clean]
rem
rem   Config        - Development (default), Shipping, Debug
rem   --skip-editor - Skip step 2.5 (Editor build), when flat Binaries already valid
rem   --clean       - Force-delete UHT headers so BuildBindings re-runs
rem
rem Pipeline:
rem   1. Build.bat SharpDemo Win64 config   - Game glue (UHT only runs on host platform)
rem   2. RunUAT PackageProject{Mobile|Mono} - publish managed DLLs (auto-detected per branch)
rem   2.5. Build.bat SharpDemoEditor Win64  - fill flat Binaries (cook needs this)
rem   3. RunUAT BuildCookRun Android        - cook + pak + stage + archive
rem
rem Requires: Visual Studio, Android NDK r27c+, .NET 10 SDK
rem ============================================================================

set "PROJ_DIR=%~dp0"
set "PROJ_DIR=%PROJ_DIR:~0,-1%"
set "PROJ_FILE=%PROJ_DIR%\SharpDemo.uproject"

rem Ensure dotnet is found even when this script is launched from Git Bash,
rem which rewrites PATH entries without the trailing backslash that
rem UnrealSharp's GetDotNetDirectory() matches against ("Program Files\dotnet\").
rem .NET 11 SDK (net11.0 managed): prefer the local RC SDK, fall back to
rem machine-wide (Program Files) once .NET 11 is installed there.
set "DOTNET11_SDK_DIR=D:\Code\DotNet11Sdk"
if not exist "%DOTNET11_SDK_DIR%\dotnet.exe" set "DOTNET11_SDK_DIR="
set "PATH=%DOTNET11_SDK_DIR%;C:\Program Files\dotnet\;%PATH%"
rem Avoid VBCSCompiler/MSBuild-server locking obj state files during the -m:1 publish.
set "DOTNET_DISABLE_BUILD_SERVERS=1"
set "PLUGIN_DIR=%PROJ_DIR%\Plugins\UnrealSharp"

rem -- Runtime mode: auto-detect Mono vs MobileClr (CoreCLR) branch --
rem   Mono branch : Build/Scripts/BuildCommands/PackageProjectMono.cs exists
rem   MobileClr   : PackageProjectMobile.cs exists (CoreCLR)
rem   (bUseMono in DefaultEngine.ini is honored by the Mono branch only; on
rem    MobileClr the ini switch is inert - CoreCLR is unconditional.)
set "PACKAGE_CMD=PackageProjectMobile"
if exist "%PLUGIN_DIR%\Build\Scripts\BuildCommands\PackageProjectMono.cs" set "PACKAGE_CMD=PackageProjectMono"

rem -- Config --
set "BUILD_CONFIG=Development"
set "SKIP_EDITOR=0"
set "CLEAN_UHT=0"

:parse_args
if "%~1"=="" goto :find_engine
if /i "%~1"=="Development"  set "BUILD_CONFIG=Development"  & shift & goto :parse_args
if /i "%~1"=="Shipping"     set "BUILD_CONFIG=Shipping"     & shift & goto :parse_args
if /i "%~1"=="Debug"        set "BUILD_CONFIG=Debug"        & shift & goto :parse_args
if /i "%~1"=="--skip-editor" set "SKIP_EDITOR=1"             & shift & goto :parse_args
if /i "%~1"=="--clean"       set "CLEAN_UHT=1"               & shift & goto :parse_args
shift & goto :parse_args

rem -- Engine path (from registry, matches .uproject EngineAssociation) --
:find_engine
set "ENGINE_GUID={8A76C2A2-4968-00D2-732B-03A4200B8146}"
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Epic Games\Unreal Engine\Builds" /v "%ENGINE_GUID%" 2^>nul ^| C:\Windows\System32\find.exe "REG_SZ"') do set "ENGINE=%%b"
if "%ENGINE%"=="" (
    echo [ERROR] Cannot find UE5 engine for GUID %ENGINE_GUID%
    exit /b 1
)
echo [INFO] Engine: %ENGINE%
echo [INFO] Config: %BUILD_CONFIG%

rem -- Android toolchain --
set "NDKROOT=C:\Users\imxql\AppData\Local\Android\Sdk\ndk\27.2.12479018"
set "ANDROID_NDK_ROOT=%NDKROOT%"
set "ANDROID_HOME=C:\Users\imxql\AppData\Local\Android\Sdk"
set "UAT=%ENGINE%\Engine\Build\BatchFiles\RunUAT.bat"
set "BUILD=%ENGINE%\Engine\Build\BatchFiles\Build.bat"

rem ============================================================================
rem Step 1 - Build native Game target (generates UHT/Game/ glue)
rem ============================================================================
echo.
echo ==============================================================
echo [Step 1/4] Building Game target (Win64, generates UHT/Game/ glue)...
echo ==============================================================
set "STEP1_ARGS=SharpDemo Win64 %BUILD_CONFIG% -Project="%PROJ_FILE%" -WaitMutex"
call "%BUILD%" %STEP1_ARGS%
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Step 1 - native Game build failed
    exit /b 1
)
echo [ OK ] Step 1 complete.

rem ============================================================================
rem Step 2 - Publish managed DLLs to Content/Managed/Android
rem ============================================================================
echo.
echo ==============================================================
echo [Step 2/4] Publishing managed DLLs (%PACKAGE_CMD%)...
echo ==============================================================
set "STEP2_ARGS=%PACKAGE_CMD% -ScriptDir="%PLUGIN_DIR%\Build\Scripts" -Project="%PROJ_FILE%" -UEBuildConfig=%BUILD_CONFIG% -TargetPlatform=Android -nocompileuat"
call "%UAT%" %STEP2_ARGS%
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Step 2 - PackageProjectMobile failed
    exit /b 1
)
echo [ OK ] Step 2 complete.

rem ============================================================================
rem Step 2.5 - Build Editor (fill flat Binaries/Managed/net11.0/)
rem ============================================================================
if "%SKIP_EDITOR%"=="1" (
    echo [SKIP] Step 2.5 - Editor build skipped via --skip-editor
    goto :step3
)

if "%CLEAN_UHT%"=="1" (
    echo [CLEAN] Deleting UHT headers to force BuildBindings...
    rmdir /s /q "%PLUGIN_DIR%\Intermediate\Build\Win64\UnrealEditor\Inc\UnrealSharpCore\UHT" 2>nul
)

echo.
echo ==============================================================
echo [Step 2.5/4] Building Editor (flat Binaries for cook)...
echo ==============================================================
set "STEP25_ARGS=SharpDemoEditor Win64 Development -Project="%PROJ_FILE%" -WaitMutex"
call "%BUILD%" %STEP25_ARGS%
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Step 2.5 - Editor build failed
    exit /b 1
)
echo [ OK ] Step 2.5 complete.

rem ============================================================================
rem Step 3 - Package APK (Cook + Pak + Stage + Archive)
rem ============================================================================
:step3
echo.
echo ==============================================================
echo [Step 3/4] Packaging APK (BuildCookRun Android)...
echo ==============================================================
set "STEP3_ARGS=BuildCookRun -project="%PROJ_FILE%" -Build -Target=SharpDemo -TargetPlatform=Android -ClientConfig=%BUILD_CONFIG% -Cook -Pak -Stage -Archive -package -nocompileuat"
call "%UAT%" %STEP3_ARGS%
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Step 3 - BuildCookRun failed
    exit /b 1
)
echo [ OK ] Step 3 complete.

rem ============================================================================
rem Verify
rem ============================================================================
echo.
echo ==============================================================
echo BUILD SUCCESSFUL
echo ==============================================================
if exist "%PROJ_DIR%\Binaries\Android\SharpDemo-arm64.apk" (
    echo APK: Binaries\Android\SharpDemo-arm64.apk
    for %%f in ("%PROJ_DIR%\Binaries\Android\SharpDemo-arm64.apk") do echo Size: %%~zf bytes
)
echo.
exit /b 0
