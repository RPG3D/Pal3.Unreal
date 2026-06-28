@echo off
setlocal enabledelayedexpansion

REM ============================================================================
REM BuildManagedCode.bat
REM Publishes managed C# DLLs for SharpDemo into Content/Managed/{Win64,Android}.
REM
REM Auto-detects the runtime backend from Config/DefaultEngine.ini [UnrealSharp]:
REM   bUseMono=true  -> Mono path: adds -p:UseMonoRuntime=true (defines UNREALSHARP_MONO
REM                    in all managed DLLs). Used by the mono202606 branch.
REM   bUseMono=false / absent -> CoreCLR path: no UseMonoRuntime flag. Used by the
REM                    AndroidClr branch (Android CoreCLR is platform-automatic;
REM                    Win64/Mac use hostfxr).
REM This lets one script serve both branches — flip bUseMono in DefaultEngine.ini
REM when switching branches.
REM ============================================================================

echo === Kill stale processes ===
taskkill /F /IM VBCSCompiler.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo === Detect runtime backend from DefaultEngine.ini ===
REM findstr extracts the bUseMono line (key name is unique enough across the whole ini).
REM Output "bUseMono=true" / "bUseMono=false"; default to false if absent.
set "BUSEMONO_LINE="
for /f "delims=" %%L in ('findstr /i /b /c:"bUseMono" "Config\DefaultEngine.ini"') do (
    set "BUSEMONO_LINE=%%L"
)
set "USE_MONO_FLAG="
if defined BUSEMONO_LINE (
    REM Extract the value after '='
    for /f "tokens=2 delims==" %%V in ("!BUSEMONO_LINE!") do set "BUSEMONO_VAL=%%V"
    set "BUSEMONO_VAL=!BUSEMONO_VAL: =!"
    if /i "!BUSEMONO_VAL!"=="true" (
        set "USE_MONO_FLAG=-p:UseMonoRuntime=true"
        echo   bUseMono=true ^> Mono path ^(UseMonoRuntime=true^)
    ) else (
        echo   bUseMono=!BUSEMONO_VAL! ^> CoreCLR path ^(no UseMonoRuntime flag^)
    )
) else (
    echo   bUseMono not found ^> CoreCLR path ^(no UseMonoRuntime flag^)
)

echo === Clean ^& prep ===
rmdir /S /Q "Content\Managed" 2>nul
mkdir "Content\Managed\Win64"
mkdir "Content\Managed\Android"

echo === Publish UnrealSharp ===
dotnet publish "Plugins\UnrealSharp\Managed\UnrealSharp\UnrealSharp.sln" -c Release --no-self-contained -p:PublishDir="%CD%\Content\Managed\Win64" -p:UseDefaultOutputPath=true -p:UETargetType=Game -p:UEBuildConfig=Shipping -p:DisableWithEditor=true !USE_MONO_FLAG!
if errorlevel 1 ( echo BUILD FAILED: UnrealSharp publish & goto :fail )

echo === Publish user scripts ===
dotnet publish "Script\ManagedSharpDemo.sln" -c Release --no-self-contained -p:PublishDir="%CD%\Content\Managed\Win64" -p:UETargetType=Game -p:UEBuildConfig=Shipping -p:DisableWithEditor=true !USE_MONO_FLAG!
if errorlevel 1 ( echo BUILD FAILED: user scripts publish & goto :fail )

echo === Verify required DLLs ===
set MISSING=0
for %%D in (ManagedSharpDemo.dll SharpDemo.RuntimeGlue.dll UnrealSharp.dll UnrealSharp.Core.dll UnrealSharp.Binds.dll UnrealSharp.Log.dll UnrealSharp.Plugins.dll UnrealSharp.StaticVars.dll) do (
    if not exist "Content\Managed\Win64\%%D" ( echo MISSING: %%D & set MISSING=1 )
)
if "%MISSING%"=="1" ( echo BUILD FAILED: required DLLs missing & goto :fail )

echo === Copy LoadOrder manifests into Content\Managed\Win64 ===
if exist "Binaries\Managed\net10.0\GlueCode.LoadOrder.json" copy /Y "Binaries\Managed\net10.0\GlueCode.LoadOrder.json" "Content\Managed\Win64\" >nul
if exist "Binaries\Managed\net10.0\UserCode.LoadOrder.json" copy /Y "Binaries\Managed\net10.0\UserCode.LoadOrder.json" "Content\Managed\Win64\" >nul

echo === Copy to Android (cross-platform IL — same DLLs work on Android) ===
xcopy /E /Y "Content\Managed\Win64\*" "Content\Managed\Android\" >nul

echo === Merge solutions ===
dotnet run --project "tools\MergeScriptSln\MergeScriptSln.csproj" -- "SharpDemo.sln" >nul

echo OK: Content\Managed\Win64 + Content\Managed\Android ready.
exit /b 0

:fail
exit /b 1
