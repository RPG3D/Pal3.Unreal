# BuildEditor — Build SharpDemoEditor (Win64 Development)

Build the UnrealEditor module for SharpDemo with Mono runtime enabled.

## Steps

1. Build C++ Editor target:
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" SharpDemoEditor Win64 Development -Project="E:/UEProject/SharpDemo/SharpDemo.uproject" -WaitMutex
```

2. Report build result. If it fails, analyze the error log:
```bash
tail -30 "E:/Code/UE5/Engine/Programs/UnrealBuildTool/Log.txt"
```
