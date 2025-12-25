@echo off
echo === Building C++ (Windows Editor + Android) ===
set TargetConfig=Development
call "E:\Code\UE5\Engine\Build\BatchFiles\Build.bat" SharpDemoEditor Win64 Development -Project="E:\UEProject\SharpDemo\SharpDemo.uproject" -WaitMutex
if %errorlevel% neq 0 goto :error
call "E:\Code\UE5\Engine\Build\BatchFiles\Build.bat" SharpDemo Android %TargetConfig% -Project="E:\UEProject\SharpDemo\SharpDemo.uproject" -WaitMutex
if %errorlevel% neq 0 goto :error

echo === Copying managed DLLs to Android (after C++ build, before Cook) ===
xcopy /E /Y "Content\Managed\Win64\*" "Content\Managed\Android\" >nul
copy /Y "Binaries\Managed\net10.0\UserCode.LoadOrder.json" "Content\Managed\Android\" >nul

echo === Cook + Pak + Stage (skipBuild) ===
rmdir /S /Q "Saved\StagedBuilds\Android" 2>nul
rmdir /S /Q "ArchivedBuilds\Android" 2>nul
call "E:\Code\UE5\Engine\Build\BatchFiles\RunUAT.bat" BuildCookRun -project="%CD%\SharpDemo.uproject" -Build -nocompileuat -TargetPlatform=Android -ClientConfig=%TargetConfig% -Cook -Iterate -Pak -Stage -Archive -package
if %errorlevel% neq 0 goto :error

echo === Done ===
goto :eof

:error
echo BUILD FAILED
pause
