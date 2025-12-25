@echo off

echo === Kill stale processes ===
taskkill /F /IM VBCSCompiler.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo === Clean & prep ===
rmdir /S /Q "Content\Managed" 2>nul
mkdir "Content\Managed\Win64"
mkdir "Content\Managed\Android"

echo === Publish UnrealSharp ===
dotnet publish "Plugins\UnrealSharp\Managed\UnrealSharp\UnrealSharp.sln" -c Release -p:UseMonoRuntime=true --no-self-contained -p:PublishDir="%CD%\Content\Managed\Win64" -p:UseDefaultOutputPath=true -p:UETargetType=Game -p:UEBuildConfig=Shipping

echo === Publish user scripts ===
dotnet publish "Script\ManagedSharpDemo.sln" -c Release -p:UseMonoRuntime=true --no-self-contained -p:PublishDir="%CD%\Content\Managed\Win64" -p:UETargetType=Game -p:UEBuildConfig=Shipping

echo === Verify ===
set MISSING=0
for %%D in (ManagedSharpDemo.dll SharpDemo.RuntimeGlue.dll UnrealSharp.dll UnrealSharp.Core.dll UnrealSharp.Binds.dll UnrealSharp.Log.dll UnrealSharp.Plugins.dll UnrealSharp.StaticVars.dll) do (
    if not exist "Content\Managed\Win64\%%D" (echo MISSING: %%D & set MISSING=1)
)
if "%MISSING%"=="1" (echo BUILD FAILED & pause & exit /b 1)

echo === Copy to Android ===
xcopy /E /Y "Content\Managed\Win64\*" "Content\Managed\Android\" >nul
copy /Y "Binaries\Managed\net10.0\UserCode.LoadOrder.json" "Content\Managed\Android\" >nul

echo OK: Content\Managed\Win64 + Content\Managed\Android ready.
