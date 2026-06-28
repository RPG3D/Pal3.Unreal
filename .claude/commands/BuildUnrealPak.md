# BuildUnrealPak — Build the UnrealPak tool (Win64 Development)

Rebuilds `UnrealPak.exe` against the current engine source. UnrealPak is an engine
program (`Engine/Source/Programs/UnrealPak/`) that is **not** rebuilt automatically
when you build project targets or the editor. After an engine upgrade (or an engine
source pull), a stale `UnrealPak.exe` can mismatch the cooked-data / shader-archive
format produced by the new engine, surfacing as packaging-time assertions such as:

```
Assertion failed: ShaderHashes.Num() == ShaderEntries.Num()
  [File: ...RenderCore/Private/ShaderCodeArchive.cpp] [Line: 628]
  UnrealPak.exe → IoStoreUtilities → FSerializedShaderArchive::Serialize
```

Run this skill **once after every engine upgrade** (or whenever you see the above
assertion). It is infrequent by design — do NOT run it as part of every package build.

## When to run

- After upgrading the engine (e.g. 5.7 → 5.8) or pulling new engine source.
- When `UnrealPak.exe` is older than `UnrealEditor.exe` / `UnrealBuildTool.dll`.
- When packaging fails with a `ShaderHashes.Num() == ShaderEntries.Num()` (or similar
  shader-archive / IoStore serialization) assertion while cook succeeds.

## Steps

1. (Optional) Check whether UnrealPak is stale before rebuilding:
```bash
ls -la "E:/Code/UE5/Engine/Binaries/Win64/UnrealPak.exe" "E:/Code/UE5/Engine/Binaries/Win64/UnrealEditor.exe"
```
If `UnrealPak.exe` is older than `UnrealEditor.exe`, it is stale — proceed.

2. Build the UnrealPak target (Development, Win64):
```bash
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" UnrealPak Win64 Development
```

3. Report build result. If it fails, analyze the error log:
```bash
tail -30 "E:/Code/UE5/Engine/Programs/UnrealBuildTool/Log.txt"
```

## Report

Verify the rebuilt binary:
- `E:/Code/UE5/Engine/Binaries/Win64/UnrealPak.exe` — mtime should now be current.

After this, re-run the failing package step (`/BuildWin64` or `/BuildAndroid`); the
shader-archive assertion should be gone.
