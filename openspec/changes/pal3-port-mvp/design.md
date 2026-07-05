## Context

SharpDemo is a UE5/6 project using the UnrealSharp plugin for C# scripting. UnrealSharp provides:
- UHT-generated C# bindings for all engine types (`FVector`, `FQuat`, `FTransform`, `AActor`, `UObject`, etc.)
- `ConfigureWithUnrealContext()` extension for async/await on the game thread
- `[UClass]`/`[UFunction]`/`[UProperty]` attributes for UE reflection
- Multiple managed module support via separate `.csproj` files

Pal3.Unity has a three-layer architecture designed for cross-engine portability:
- **Pal3.Core** (292 files) — Pure C#, zero engine dependency. Custom math types (`GameBoxVector3`), binary data readers (54 file formats), SCE command system (192 command classes), CPK file system, A* pathfinding.
- **Engine** (55 files) — Unity abstraction layer. Defines interfaces (`IGameEntity`, `ITransform`, `IMaterial`, `ITexture2D`, `ISprite`) and Unity implementations. **However**, the interfaces leak Unity types: `ITransform` uses `UnityEngine.Vector3` and `UnityEngine.Quaternion` in its method signatures.
- **Pal3.Game** (219 files) — Full gameplay. Depends on Engine interfaces + Unity types directly in some places (`MonoBehaviour`, `Coroutine`, `UnityEngine.UI`, `TextMeshPro`, Input System, PostProcessing).

The key insight: Pal3.Core can be copied verbatim. The Engine interfaces must be re-implemented against UE, and a `UnityEngine` namespace must provide the leaked Unity types as thin wrappers around UnrealSharp/UE counterparts.

**Stakeholders**: SharpDemo project owner; Pal3.Unity upstream author (planned feedback on interface leak issue).

## Goals / Non-Goals

**Goals (MVP scope):**
- `Pal3.Core` compiles as a UnrealSharp module with zero source changes.
- `UnityEngine` compat layer provides the ~15 Unity API surface types that Pal3's Engine and Game code reference.
- `Pal3.Engine` re-implements the Engine abstraction interfaces against UE types.
- One simple `SceneObject` (e.g., `Door`) runs in UE, proving the full stack works: entity creation, transform manipulation, async coroutine patterns, coordinate conversion.
- Existing SharpDemo modules compile and run without regression.

**Non-Goals (deferred to follow-up changes):**
- Full Pal3.Game port (219 files). This change establishes infrastructure; follow-up changes port subsystems incrementally.
- UMG UI port (all Unity uGUI → UMG rewrite).
- Enhanced Input System migration (Unity Input System → UE Enhanced Input).
- Audio/Video system port.
- CPK asset loading from original game files (requires game data to be staged).
- Full coroutine conversion (449 sites). Only the sites needed for the MVP SceneObject are converted.
- Upstreaming Engine interface fixes to Pal3.Unity (separate GitHub discussion).
- Android / Mac platform support for Pal3 modules.

## Decisions

### Decision 1: `UnityEngine` as a separate managed module (not namespace in Pal3.Engine)
**Choice:** Create a standalone `UnityEngine.csproj` module that Pal3.Engine and Pal3.Game reference.

**Why:** Clean separation of concerns. The `UnityEngine` module can be reused for other Unity→UE ports. It has no dependency on Pal3 types. If upstream Pal3.Unity later removes Unity types from its interfaces, the `UnityEngine` module can still serve as a compat shim for any remaining direct Unity API usage in Pal3.Game.

**Alternatives considered:** *Put `UnityEngine` types inside Pal3.Engine.* Rejected — tangles compat concerns with Engine implementation, harder to maintain/test independently.

### Decision 2: `UnityEngine.Vector3` stores `float` internally (not `double`)
**Choice:** `UnityEngine.Vector3` uses `float` fields (`X`, `Y`, `Z`), matching Unity's `Vector3`. Provides `implicit operator` conversions to/from UnrealSharp's `FVector` (which uses `double`).

**Why:** Pal3's coordinate values after `GameBoxUnitToUnityUnit` scaling (÷20) are well within `float` precision. Using `float` avoids silent precision changes in math-heavy code (pathfinding, physics queries). The implicit conversion handles the `float`↔`double` boundary transparently.

**Alternatives considered:** *Use `double` internally.* Rejected — changes semantics of all Pal3 math code; `float`-specific code paths (e.g., `Mathf.Approximately`) would break.

### Decision 3: Coroutine compatibility via `CoroutineCompatibility` static helpers
**Choice:** Provide `UnityEngine.CoroutineCompatibility` with static methods (`WaitForSeconds`, `WaitUntil`, `NextFrame`) that return `Task` and internally use `ConfigureWithUnrealContext()`. Pal3.Game coroutines are mechanically rewritten from `IEnumerator` → `async Task`.

**Why:** UnrealSharp's async pattern (`await Task.Delay(n).ConfigureWithUnrealContext()`) is the documented equivalent to Unity coroutines. Wrapping it in a familiar API reduces mechanical conversion errors. `ConfigureWithUnrealContext()` ensures continuation runs on the game thread.

**Alternatives considered:** *Use UE `FLatentActionInfo` / `SystemLibrary.SetTimer` directly.* Rejected — more verbose, less familiar to developers coming from Unity, and the async/await pattern is cleaner for sequential logic.

### Decision 4: MVP uses a native-C++ `AActor` subclass as the managed entity host
**Choice:** The `UEGameEntity` wraps an `AActor` spawned via `UWorld::SpawnActor`. The `UETransform` wraps the actor's `RootComponent` transform. No new native C++ classes are created for the MVP — use existing UE actor/component types.

**Why:** Minimizes the change surface. UnrealSharp already supports spawning actors and accessing their components from C#. If performance or functionality gaps emerge (e.g., needing a custom `AActor` subclass for specific tick behavior), that becomes a follow-up change.

**Alternatives considered:** *Create a native `ACSEntity` C++ class with dedicated tick/event hooks.* More powerful but adds C++ compilation dependency; defer until needed.

### Decision 5: Pal3.Core source files copied, not linked/submoduled
**Choice:** Copy the 292 Pal3.Core `.cs` files into `Script/Pal3.Core/` as first-class source files in this repo, preserving the original namespace layout (`Pal3.Core.*`).

**Why:** Pal3.Unity is a separate git repo with its own license (GPL-3.0). Submoduling would couple SharpDemo's build to an external repo's structure. Copying gives us control over the files while respecting the license. The original `Pal3.Core` namespace is preserved to minimize diffs when syncing upstream changes.

**Alternatives considered:** *Git submodule.* Rejected — Pal3.Unity is a full Unity project; submoduling brings in Unity-specific files and complicates build. *Symlinks.* Not portable on Windows.

### Decision 6: Coordinate conversion moved from `UnityPrimitivesConvertor` to `UnrealPrimitivesConvertor`
**Choice:** Create `Pal3.Engine.Extensions.UnrealPrimitivesConvertor` with the same API surface as the original `UnityPrimitivesConvertor`, but producing `UnityEngine.Vector3`/`Quaternion` (which are thin wrappers around `FVector`/`FQuat`). The scaling constants (`GameBoxUnitToUnityUnit = 20f`, etc.) are preserved verbatim.

**Why:** The conversion math is identical — GameBox uses its own coordinate system regardless of the target engine. The only difference is the output type. Renaming avoids confusion and makes it clear this is the UE variant.

**Alternatives considered:** *Keep the original name.* Rejected — misleading since we're no longer targeting Unity.

### Decision 7: Phase-gated implementation with MVP checkpoint
**Choice:** Tasks are ordered in 4 phases: (1) project skeleton, (2) UnityEngine types, (3) Engine UE implementation, (4) MVP SceneObject validation. Each phase must complete before the next begins. Phase 4 is the go/no-go checkpoint for the overall approach.

**Why:** Phase dependencies are linear (Engine needs UnityEngine, Game needs Engine). The MVP checkpoint ensures we validate the architecture before committing to the full 219-file Pal3.Game port.

## Architecture

### Module Dependency Graph

```
┌─────────────────────────────────────┐
│  UnrealSharp (plugin)               │
│  ├── UHT-generated SharpDemo.dll    │  FVector, FQuat, FTransform,
│  │   (UE type bindings)             │  AActor, UObject, UWorld, ...
│  ├── UnrealSharp.dll                │  [UClass], [UFunction], async,
│  │   (runtime + extensions)         │  Delegate, GCHandle utils
│  └── UnrealSharp.Core.dll           │  UObject base, attributes
└──────────────┬──────────────────────┘
               │
    ┌──────────┼──────────────────────────────┐
    │          │                              │
    v          v                              v
┌──────────┐ ┌──────────────────┐  ┌─────────────────────┐
│Pal3.Core │ │  UnityEngine     │  │ ManagedSharpDemo    │
│(net10.0) │ │  (net10.0)       │  │ (existing, unchanged)│
│          │ │                  │  └─────────────────────┘
│ 零外部依赖│ │ wraps FVector,   │
│          │ │ FQuat, etc. as   │
│ GameBox  │ │ Unity type names │
│ math types│ │                  │
│ DataReader│ │ CoroutineCompat  │
│ Commands │ │ Debug, Time,     │
│ FileSystem│ │ Mathf wrappers   │
└─────┬────┘ └────────┬─────────┘
      │               │
      └───────┬───────┘
              │ both referenced by
              v
┌──────────────────────────────┐
│  Pal3.Engine (net10.0)       │
│                              │
│  Implements IGameEntity,     │
│  ITransform, IMaterial,      │
│  ITexture2D, ISprite, etc.   │
│  against UE types via        │
│  UnityEngine wrappers        │
│                              │
│  UnrealPrimitivesConvertor   │
│  ServiceLocator (adapted)    │
│  UEPhysicsManager             │
└──────────────┬───────────────┘
               │
               v
┌──────────────────────────────┐
│  Pal3.Game (net10.0)         │  ← Phase 4+ (MVP: 1 SceneObject)
│                              │
│  SceneObjects, GameSystems,  │
│  Rendering, Input, UI,       │
│  Audio, Combat, Dialogue     │
└──────────────────────────────┘
```

### UnityEngine Type Mapping

| Unity API | UnrealSharp Backing | Notes |
|-----------|-------------------|-------|
| `Vector3` (struct, float) | `FVector` (struct, double) | implicit conversion; float↔double at boundaries |
| `Quaternion` (struct, float) | `FQuat` (struct, double) | implicit conversion |
| `Vector2` (struct, float) | `FVector2D` (struct, double) | implicit conversion |
| `Vector2Int` (struct, int) | `FVector2i` (?) or custom struct | check if UHT generates this |
| `Color` (struct, float) | `FLinearColor` | field mapping |
| `Color32` (struct, byte) | `FColor` | field mapping |
| `Matrix4x4` | `FMatrix` | field mapping |
| `Debug.Log/LogWarning/LogError` | `LogUnrealSharp.Log/LogWarning/LogError` | static methods |
| `Time.deltaTime` | `UWorld::GetDeltaSeconds()` via async context | static property |
| `Time.time` | `UWorld::GetTimeSeconds()` | static property |
| `Mathf` (static) | `MathF` / `FMath` | static methods, mostly pass-through |
| `PlayerPrefs` | `GConfig` (UE config system) | key-value store |
| `Application.persistentDataPath` | `FPaths::ProjectSavedDir()` | static property |
| `Application.version` | `FEngineVersion::Current()` | static property |
| `Object.Destroy(obj)` | `UObject::Destroy()` or `AActor::K2_DestroyActor()` | extension method |
| `Object.Instantiate` | `SpawnActor<T>()` or `NewObject<T>()` | extension method |

### Coroutine Conversion Pattern

```csharp
// Before (Unity):
IEnumerator DoSomething()
{
    yield return new WaitForSeconds(1f);
    yield return null;
    DoWork();
}

// After (UnrealSharp):
async Task DoSomething(CancellationToken ct = default)
{
    await CoroutineCompatibility.WaitForSeconds(1f, ct);
    await CoroutineCompatibility.NextFrame(ct);
    if (ct.IsCancellationRequested) return;
    DoWork();
}

// Where:
public static class CoroutineCompatibility
{
    public static async Task WaitForSeconds(float seconds, CancellationToken ct = default)
        => await Task.Delay((int)(seconds * 1000), ct).ConfigureWithUnrealContext();

    public static async Task NextFrame(CancellationToken ct = default)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
    }

    public static async Task WaitUntil(Func<bool> predicate, CancellationToken ct = default)
    {
        while (!predicate())
        {
            await NextFrame(ct);
        }
    }
}
```

### Coordinate System Translation

Preserved from Pal3's existing `UnityPrimitivesConvertor`:

```
GameBox → Engine Unit:   ÷ 20  (GameBoxUnitToUnityUnit)
GameBox MV3 → Unit:      ÷ 1270 (GameBoxMv3UnitToUnityUnit)
GameBox Ticks → Seconds: ÷ 4800 (GameBoxTicksPerSecond)

Axis mapping (GameBox → Unity, carried to UE):
  X: -gameBox.X / scale    (flip X)
  Y:  gameBox.Y / scale
  Z:  gameBox.Z / scale

Unity Y-up vs UE Z-up note:
  The GameBox→Unity conversion already handles axis remapping.
  Since UE uses Z-up (not Y-up like Unity), additional remapping
  may be needed for the Y→Z axis swap. This is verified during
  Phase 4 MVP when the first SceneObject is placed in a UE level.
```

## Risks / Trade-offs

- **[Engine interface leak]** → The `ITransform` interface uses `UnityEngine.Vector3`/`Quaternion` directly. Our `UnityEngine` shim solves this at the type level, but the interfaces still carry Unity type names. Long-term fix: upstream the interface change to use a math abstraction. Short-term: this works transparently.
- **[Coroutine conversion scale]** → 449 sites across 90 files. The MVP converts only ~5-10 sites for one SceneObject. A follow-up change should explore scripting the bulk conversion.
- **[Coordinate axis swap (Y-up → Z-up)]** → Unity uses Y-up, UE uses Z-up. The GameBox→Unity conversion maps GameBox Y to Unity Y. For UE, we may need to map GameBox Y → UE Z. This is validated during Phase 4 and corrected if needed.
- **[UMG UI blocking MVP scope]** → Pal3 uses extensive Unity uGUI. This is deferred entirely; the MVP SceneObject should be chosen to have minimal/no UI dependency.
- **[Asset loading not in MVP]** → Pal3 reads original game files (.cpk, .pol, .scn). For the MVP, test assets can be procedurally generated (simple meshes/textures) — no original game data needed to validate the architecture.
- **[UnrealSharp UHT glue may miss types]** → If UnrealSharp's glue generator doesn't expose a UE type we need for the Engine implementations, we may need to add native C++ glue. Risk is low for common types (AActor, USceneComponent, UMaterial, UTexture2D).

## Migration Plan

1. Copy `Pal3.Core` source files into `Script/Pal3.Core/`; create `.csproj`.
2. Create `UnityEngine` module with thin wrappers for the ~15 Unity types.
3. Create `Pal3.Engine` module implementing Engine interfaces against UE.
4. Create `Pal3.Game` module skeleton with one MVP SceneObject.
5. Build and verify SharpDemo compiles with all new modules.
6. Launch editor, place the MVP SceneObject in a level, verify runtime behavior.
7. Validate coordinate conversion by comparing rendered position against expected Pal3 coordinates.

**Rollback:** Remove the four new `Script/*/` directories and their `.csproj` files. Existing modules are untouched.

## Open Questions

- Does UnrealSharp's UHT glue generator expose `FVector2i` (int-based 2D vector)? If not, `UnityEngine.Vector2Int` needs a hand-written struct.
- What is the exact axis mapping for UE's Z-up coordinate system vs Unity's Y-up? The `UnityPrimitivesConvertor` maps GameBox→Unity; the Unity→UE axis swap (Y→Z, Z→Y) must be verified with a test object.
- Should `Pal3.Engine` keep the original Engine source directory layout, or reorganize for UE conventions? (Recommend: mirror the original layout for diff-friendliness.)
- For the MVP SceneObject, which specific Pal3 scene object (Door, Switch, Chest, Elevator?) has the simplest dependency footprint? (Need to audit each object's dependencies on UI, audio, input, coroutines.)
