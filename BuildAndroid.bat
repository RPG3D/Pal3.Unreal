@echo off
setlocal enabledelayedexpansion

rem ============================================================================
rem BuildAndroid.bat - One-shot Android packaging (runtime auto-detected per branch)
rem
rem Usage:
rem   BuildAndroid.bat [Config] [--skip-editor] [--clean]
rem
rem   Config        - Development (default), Shipping, Debug
rem   --skip-editor - Skip step 2.5 (Editor build), when flat Binaries already valid
rem   --clean       - Force-delete UHT headers so BuildBindings re-runs
rem
rem Pipeline (three logical phases, each with a host-side prerequisite):
rem   1.  Build.bat SharpDemo Win64 config    - PREREQ of phase A: only a Game-target
rem                                             UBT build generates UHT/Game glue, which
rem                                             the managed publish (phase A) consumes.
rem   A.  RunUAT PackageProject{Mobile|Mono}  - compile managed DLLs -> Content/Managed/
rem                                             Android/ (auto-detected per branch).
rem   2.5 Build.bat SharpDemoEditor Win64     - PREREQ of phase B: cook runs
rem                                             UnrealEditor-Cmd, whose UnrealSharp runtime
rem                                             loads plugin managed assemblies from flat
rem                                             Binaries/Managed/<tfm>/ (backfilled by the
rem                                             editor-target [UnrealSharp] build step) and
rem                                             guarantees editor binaries exist.
rem   B.  RunUAT BuildCookRun Android         - standard UAT packaging: build (Android
rem                                             target) + cook + pak + stage + archive,
rem                                             ending with gradle producing the APK.
rem
rem Steps 1 and 2.5 are idempotent/incremental (~40s when warm); neither is skippable
rem on a cold tree (glue / flat managed would be missing or stale).
rem
rem Requires: Visual Studio, Android NDK r28 (UE 5.9 minimum), .NET 10 SDK
rem ============================================================================

set "PROJ_DIR=%~dp0"
set "PROJ_DIR=%PROJ_DIR:~0,-1%"
set "PROJ_FILE=%PROJ_DIR%\SharpDemo.uproject"

rem Ensure dotnet is found even when this script is launched from Git Bash,
rem which rewrites PATH entries without the trailing backslash that
rem UnrealSharp's GetDotNetDirectory() matches against ("Program Files\dotnet\").
set "DOTNET_DISABLE_BUILD_SERVERS=1"
set "PLUGIN_DIR=%PROJ_DIR%\Plugins\UnrealSharp"

rem mobile20261120 publishes application assemblies independently of the runtime.
rem DotNetRuntimeSDK stages the matching Mono/CoreCLR BCL and native library.
set "PACKAGE_CMD=PackageProjectMobile"


rem The plugin pins .NET 10 through Managed/global.json.
set "PATH=C:\Program Files\dotnet\;%PATH%"

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
set "ENGINE_GUID="
for /f "tokens=2 delims=:, " %%A in ('findstr /i "EngineAssociation" "%PROJ_FILE%"') do set "ENGINE_GUID=%%~A"
if "%ENGINE_GUID%"=="" set "ENGINE_GUID={8A76C2A2-4968-00D2-732B-03A4200B8146}"
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Epic Games\Unreal Engine\Builds" /v "%ENGINE_GUID%" 2^>nul ^| C:\Windows\System32\find.exe "REG_SZ"') do set "ENGINE=%%b"
if "%ENGINE%"=="" (
    echo [ERROR] Cannot find UE5 engine for GUID %ENGINE_GUID%
    exit /b 1
)
echo [INFO] Engine: %ENGINE%
echo [INFO] Config: %BUILD_CONFIG%

rem -- Android toolchain --
set "NDKROOT=C:\Users\imxql\AppData\Local\Android\Sdk\ndk\28.0.13004108"
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
set "GAME_BUILD_RESULT=%ERRORLEVEL%"
if %GAME_BUILD_RESULT% neq 0 (
    echo [FAIL] Step 1 - native Game build failed
    exit /b 1
)
echo [ OK ] Step 1 complete.

rem ============================================================================
rem Step 2 - Publish managed DLLs to Content/Managed/Android
rem ============================================================================
echo.
echo ==============================================================
echo [Step 2/4] Publishing complete managed bundle...
echo ==============================================================
dotnet run --project "%PROJ_DIR%\tools\ManagedModules\ModulePublisher.csproj" -- Android all --configuration %BUILD_CONFIG% --architecture arm64 --publish-host --engine-root "%ENGINE%"
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Step 2 - %PACKAGE_CMD% failed
    exit /b 1
)
echo [ OK ] Step 2 complete.

rem ============================================================================
rem Step 2.5 - Build Editor (fill flat Binaries/Managed/<tfm> for cook)
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
echo [Step 2.5/4] Building Editor (backfill flat Binaries for cook)...
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
