## Why

Pal3.Unity (`E:\Code\Pal3.Unity`) is an open-source re-implementation of the classic Chinese RPG "The Legend of Sword and Fairy 3" (PAL3 / 仙剑奇侠传三) in C# / Unity 6. It has a cleanly layered architecture: **Pal3.Core** (292 files, pure C#, zero Unity dependency), **Engine** (55 files, Unity abstraction layer), **Pal3.Game** (219 files, gameplay). The author designed the Engine layer explicitly for cross-engine portability. SharpDemo already has UnrealSharp integration, providing C# scripting on UE5/6 with .NET 10. This change establishes the project skeleton and minimum viable modules to begin porting Pal3 to Unreal Engine via UnrealSharp.

## What Changes

- **New managed module: `Pal3.Core`** — copied from `E:\Code\Pal3.Unity\Assets\Scripts\Pal3.Core\` into `Script/Pal3.Core/`. Zero Unity dependencies; compiles as-is on `net10.0`. Contains game-agnostic logic: data readers, SCE commands, math primitives, file system, algorithms.
- **New managed module: `UnityEngine`** — created at `Script/UnityEngine/`. A thin compatibility layer (~15 types) that wraps UE APIs behind Unity-identical type names (`UnityEngine.Vector3`, `UnityEngine.Quaternion`, `UnityEngine.Debug`, `UnityEngine.Time`, etc.). Scoped to only the Unity APIs actually used by Pal3's Engine interfaces and Pal3.Game code.
- **New managed module: `Pal3.Engine`** — created at `Script/Pal3.Engine/`. Re-implements Pal3's Engine abstraction interfaces (`IGameEntity`, `ITransform`, `IMaterial`, `ITexture2D`, `ISprite`, etc.) against UE types via UnrealSharp. Reuses Pal3's existing `ServiceLocator` pattern and logging utilities with minimal adaptation.
- **Coroutine → async pattern library** — `UnityEngine.CoroutineCompatibility` providing `WaitForSeconds`, `WaitUntil`, `NextFrame` as async methods backed by UnrealSharp's `ConfigureWithUnrealContext()` and `Task.Delay`, enabling mechanical conversion of Pal3.Game's 449 coroutine usages.
- **Phase-4 MVP validation** — port one simple `SceneObject` (e.g., Door or Switch) from Pal3.Game to verify the entire stack compiles, actor spawning works, and coordinate systems align correctly.

## Capabilities

### New Capabilities
- `pal3-core-module`: Pal3.Core compiles and runs as a UnrealSharp managed module targeting `net10.0`, with no modifications to the original source files.
- `unityengine-compat-layer`: A `UnityEngine` namespace shim provides Unity-compatible APIs (Vector3, Quaternion, Debug, Time, Mathf, CoroutineCompatibility, etc.) backed by UnrealSharp / UE types, sufficient for Pal3's Engine interfaces and Game code to compile against.
- `pal3-engine-ue-impl`: The Pal3 Engine abstraction interfaces (`IGameEntity`, `ITransform`, `IMaterial`, `ITexture2D`, `ISprite`, `ServiceLocator`, etc.) are implemented against UE via UnrealSharp, supporting entity creation, transform manipulation, component attachment, and service registration.

### Modified Capabilities
<!-- None — Pal3 code is new to this project; existing SharpDemo modules are unaffected. -->

## Impact

- **Code**: `Script/Pal3.Core/` (292 files copied), `Script/UnityEngine/` (~15 new files), `Script/Pal3.Engine/` (~25 new/reimplemented files), `Script/Pal3.Game/` (219 files adapted, mostly Phase 4+). Existing `Script/ManagedSharpDemo/` and `Script/SharpDemo.RuntimeGlue/` are unchanged.
- **Build**: Three new `.csproj` files (`Pal3.Core.csproj`, `UnityEngine.csproj`, `Pal3.Engine.csproj`) targeting `net10.0`, importing `UnrealSharp.Shared.props`. The `MergeScriptSln` tool may need updates to include these projects in the merged `.sln`.
- **Dependencies**: `UnityEngine` references the UHT-generated `SharpDemo.dll` (for FVector, FQuat, FTransform, etc.). `Pal3.Engine` references `UnityEngine` + `Pal3.Core`. `Pal3.Game` references all three.
- **Submodule**: UnrealSharp plugin (`Plugins/UnrealSharp`) is unchanged by this change. If C++ UMG helpers are needed for Phase 4 UI work, that becomes a separate change.
- **Risk**: Coroutine conversion (449 usages across 90 files) is the largest mechanical effort. The MVP defers the bulk to post-Phase-4 follow-up work, limiting this change's scope to infrastructure + one SceneObject.
