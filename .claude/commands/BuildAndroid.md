# BuildAndroid — Build + Package Android (Development, arm64)

Full Android build pipeline: managed DLLs → C++ (Editor + Android) → UAT Cook+Pak+Stage+Archive.

## Steps

### 1. Build managed C# DLLs
```bash
cd "E:/UEProject/SharpDemo" && cmd /c "BuildManagedCode.bat"
```

### 2. Build C++ (Editor + Android)
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" SharpDemoEditor Win64 Development -Project="E:/UEProject/SharpDemo/SharpDemo.uproject" -WaitMutex
```
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" SharpDemo Android Development -Project="E:/UEProject/SharpDemo/SharpDemo.uproject" -WaitMutex
```

### 3. Copy managed DLLs to Android + LoadOrder.json
```bash
cp -r "E:/UEProject/SharpDemo/Content/Managed/Win64/"* "E:/UEProject/SharpDemo/Content/Managed/Android/"
cp "E:/UEProject/SharpDemo/Binaries/Managed/net10.0/UserCode.LoadOrder.json" "E:/UEProject/SharpDemo/Content/Managed/Android/"
```

### 4. Package (Cook + Pak + Stage + Archive)
Clean previous builds first:
```bash
rm -rf "E:/UEProject/SharpDemo/Saved/StagedBuilds/Android" "E:/UEProject/SharpDemo/ArchivedBuilds/Android"
```
Then run UAT:
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/RunUAT.bat" BuildCookRun -project="E:/UEProject/SharpDemo/SharpDemo.uproject" -Build -Target=SharpDemo -TargetPlatform=Android -ClientConfig=Development -Cook -Pak -Stage -Archive -package -nocompileuat
```

### 5. Report
Verify APK exists at `ArchivedBuilds/Android/SharpDemo-arm64.apk` and report file size.
