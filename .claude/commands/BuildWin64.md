# BuildWin64 — Build + Package Win64 (Development)

Full Win64 package pipeline: managed DLLs → C++ → UAT Cook+Pak+Stage.

## Steps

### 1. Build managed C# DLLs
```bash
cd "E:/UEProject/SharpDemo" && cmd /c "BuildManagedCode.bat"
```

### 2. Build C++ Game target
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" SharpDemo Win64 Development -Project="E:/UEProject/SharpDemo/SharpDemo.uproject" -WaitMutex
```

### 3. Package (Cook + Pak + Stage)
Use `-skipBuild` since C++ was already built in step 2:
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/RunUAT.bat" BuildCookRun -project="E:/UEProject/SharpDemo/SharpDemo.uproject" -skipBuild -TargetPlatform=Win64 -ClientConfig=Development -Cook -Pak -Stage
```

### 4. Report
Verify staged output:
- `Saved/StagedBuilds/Windows/SharpDemo.exe`
- `Saved/StagedBuilds/Windows/SharpDemo/Content/Paks/SharpDemo-Windows.pak`
- BCL DLLs staged as NonUFS, project DLLs in PAK (UFS)
