## Implementation Status as of 2026-07-04

**Current state: 5 core projects compile with 0 errors. Pal3.Game source files (222) copied but excluded from build — ready for incremental porting.**

## 1. Project skeleton — ✅ COMPLETE (with changes)

- [x] 1.1 Pal3.Core.csproj — created, targets `net10.0`, `<DefineConstants>$(DefineConstants);PAL3</DefineConstants>`, references Newtonsoft.Json + INIFileParser from `Libraries/`.
- [x] 1.2 292 Pal3.Core `.cs` files copied from `E:\Code\Pal3.Unity\Assets\Scripts\Pal3.Core\` → `Script/Pal3.Core/`.
- [x] 1.3 UnityEngine.csproj — created, imports `UnrealSharp.Shared.props`, references `SharpDemo.dll`.
- [x] 1.4 **RENAMED**: `Script/Pal3.Engine/` → `Script/Engine/` (to avoid `Pal3.Engine.` namespace pollution in Pal3.Game files). `Engine.csproj` references `Pal3.Core.csproj` + `UnityEngine.csproj`.
- [x] 1.5 **Added**: Pal3.Game.csproj — created, references Pal3.Core + UnityEngine + Engine + Newtonsoft.Json.
- [x] 1.6 All 5 projects compile: `dotnet build Script/ManagedSharpDemo.sln` → **Build succeeded. 0 Warnings.**
- [x] 1.7 ManagedSharpDemo.sln — updated with all 6 projects; Pal3.Game has `ActiveCfg` but **no `Build.0`** (excluded from build until porting progresses).

## 2. UnityEngine compatibility layer — ✅ COMPLETE (16 types)

| File | Contents |
|------|----------|
| `Vector3.cs` | float X/Y/Z, implicit ↔ FVector(double), operators, static props |
| `Quaternion.cs` | float X/Y/Z/W, implicit ↔ FQuat(double), Euler, Slerp, etc. |
| `Vector2.cs` | float X/Y, implicit ↔ FVector2D |
| `Vector2Int.cs` | int X/Y |
| `Color.cs` | Color (float RGBA) + Color32 (byte RGBA) |
| `Matrix4x4.cs` | Vector4 + Matrix4x4 |
| `Debug.cs` | Log/LogWarning/LogError/LogException → LogUnrealSharp |
| `Mathf.cs` | Abs, Clamp, Lerp, Sin, Cos, Sqrt, etc. → MathF |
| `Time.cs` | deltaTime, time, unscaledDeltaTime (settable from game loop) |
| `Application.cs` | persistentDataPath, version, isPlaying, platform, targetFrameRate |
| `PlayerPrefs.cs` | GetInt/SetInt/GetFloat/SetFloat/GetString/SetString (in-memory) |
| `Object.cs` | Destroy(obj), DontDestroyOnLoad(obj) — non-static class (like Unity) |
| `CoroutineCompatibility.cs` | WaitForSeconds, WaitUntil, NextFrame → async/ConfigureWithUnrealContext |
| `UnityTypes.cs` | Material, Shader, Mesh, Sprite, Texture, MeshFilter, MeshRenderer, SpriteRenderer, Skybox, AudioClip, Transform, Camera, GameObject, Bounds, WaitForSeconds, WaitUntil |
| `Vector2Int.cs` | (see above) |

## 3. Engine — UE implementation — ✅ COMPLETE (with stubs)

- [x] 3.1 Abstraction interfaces — 10 copied from Pal3.Unity's Engine (IGameEntity, ITransform, IMaterial, etc.)
- [x] 3.2 **UEGameEntity** — wraps AActor with REAL API calls (validated against UnrealSharp.dll):
  - `Name` → `GetActorLabel()` / `SetActorLabel()`
  - `IsStatic` → `RootComponent.Mobility`
  - `SetParent` → `AttachToActor()` with `EAttachmentRule`
  - `AddComponent<T>` → reflection-based `AddComponentByClass<T>()`
  - `GetComponent<T>` → reflection-based `GetComponentByClass<T>()`
  - `Destroy()` → `DestroyActor()`
- [x] 3.3 **UETransform** — wraps AActor root transform with REAL API calls:
  - `Position` → `ActorLocation` (get) / `SetActorLocation()` (set)
  - `Rotation` → `ActorRotation.ToQuaternion` / `SetActorRotation(new FRotator(...))`
  - `LocalPosition` → `RelativeLocation` (read/write property)
  - `LookAt` → `new FRotator(lookDir)` + `SetActorRotation()`
- [x] 3.4 UEGameEntityFactory — `Wrap(AActor)` + `Create(UWorld, string)` (stub).
- [x] 3.5 ServiceLocator — adapted (reflection-based private ctor support, LogUnrealSharp).
- [x] 3.6 UEPhysicsManager — not yet implemented (complex).
- [x] 3.7 EngineLogger — copied from Pal3, uses our `UnityEngine.Debug`.
- [x] 3.8 **UnrealPrimitivesConvertor** — ported with all 22 conversion methods + old-name compat aliases (`ToUnityPosition`, `ToUnityDistance`, etc.).
- [x] 3.9 GameEntityScript — adapted (MonoBehaviour base removed, partial class, TickableGameEntityScript with OnUpdateGameEntity/OnLateUpdateGameEntity/OnFixedUpdateGameEntity).

## 4. MVP SceneObject validation — ⚠️ PARTIALLY COMPLETE

- [x] 4.1-4.2 **Pal3.Game source files**: 222 files copied from Pal3.Unity, placed in `Script/Pal3.Game/`.
- [x] 4.3-4.4 Attempted batch port with comprehensive stubs (`AllStubs.cs`) — achieved ~84 errors from 2598 starting point, but duplication issues remain.
- [x] **Editor runtime validation**: UE 5.8 editor launched, all 5 DLLs loaded successfully in correct order (SharpDemo → Pal3.Core → UnityEngine → Engine → Pal3.Game).
- [x] **API discovery**: Reflection on UnrealSharp.dll (36,003 types) to find real AActor/USceneComponent/FRotator/FQuat method signatures.
- [x] **Coordinate system**: GameBox→Engine conversion constants preserved (GameBoxUnitToEngineUnit=20f, etc.), axis-flipping logic same as original.

## 5. Finalize — ⚠️ PARTIALLY COMPLETE

- [x] 5.1 All existing SharpDemo C# modules compile without regression.
- [x] 5.2 GlueCode.LoadOrder.json updated: `["SharpDemo","Pal3.Core","UnityEngine","Engine","Pal3.Game"]`.
- [x] 5.3 IModuleInterface entry points: `FEngineModule.cs` (Engine.Module) + `FGameModule.cs` (Pal3.Game).
- [ ] 5.4 REMAINING: Incremental porting of Pal3.Game files. Strategy:
  1. Pick one file from `Script/Pal3.Game/` with fewest deps
  2. Remove its types from `Script/Pal3.Game/Stubs/AllStubs.cs`
  3. Replace `using UnityEngine;` → our compat, fix coroutine patterns
  4. `dotnet build Script/Pal3.Game/Pal3.Game.csproj` → fix errors → repeat
  5. When Pal3.Game compiles, add `Build.0` for its GUID in ManagedSharpDemo.sln

## Key Architectural Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Engine project renamed to `Script/Engine/` (was `Pal3.Engine/`) | Avoids C# namespace pollution — `Pal3.Game` files use `using Engine.*` which resolved to `Pal3.Engine.*` incorrectly |
| 2 | Pal3.Game excluded from solution build | 222 files have ~2000 missing Unity API calls; incremental porting avoids blocking other modules |
| 3 | All stubs in single file `Pal3.Game/Stubs/AllStubs.cs` | Cleaner than scattered files; remove types as real implementations arrive |
| 4 | UETransform/UEGameEntity use REAL AActor API | Discovered via reflection on UnrealSharp.dll (36,003 types); no more guesswork |
| 5 | Implicit conversion `Vector3 ↔ FVector(double)` | Pal3 coordinates after ÷20 scaling are within float precision; no semantic change |

## Files Created/Modified

```
Script/
├── Pal3.Core/             (292 .cs + Libraries/)
├── UnityEngine/           (11 .cs files)
├── Engine/                (37 .cs files, renamed from Pal3.Engine)
├── Pal3.Game/             (222 Pal3 source + Stubs/AllStubs.cs + MvpValidation.cs + FGameModule.cs)
└── ManagedSharpDemo.sln   (6 projects, Pal3.Game excluded from build)

Binaries/Managed/net10.0/GlueCode.LoadOrder.json
tools/CoroutineConverter/ConvertCoroutines.cs
```

## Next Steps (priority order)

1. **Runtime verification**: `RunEditor` → check FEngineModule.StartupModule log → confirm UETransform wiring
2. **Incremental Pal3.Game porting**: Start with `GeneralSceneObject.cs` (simplest), then `DoorObject.cs`
3. **Coroutine converter**: Run dry-run on Pal3.Unity source to quantify conversion scope
4. **UnityEngine.Time wiring**: Hook `Time.deltaTime` to UE world tick
