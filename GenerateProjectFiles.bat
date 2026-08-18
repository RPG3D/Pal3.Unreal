@echo on

call "D:\Code\UE5\Engine\Build\BatchFiles\GenerateProjectFiles.bat" %~dp0/SharpDemo.uproject -game -engine -VSCode
call "D:\Code\UE5\Engine\Build\BatchFiles\GenerateProjectFiles.bat" %~dp0/SharpDemo.uproject -game -engine -VS2026

pause

