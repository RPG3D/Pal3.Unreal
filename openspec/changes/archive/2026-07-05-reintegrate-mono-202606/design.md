# Design — Re-integrate Mono onto `mono202606` (current upstream tip)

## The key insight that makes this cheap

Investigation of the submodule reflog revealed that the canonical Mono work was never lost — it survives in the object database — and a **prior re-integration attempt already did the hard part**.

```
Submodule Plugins/UnrealSharp, branch mono202606 reflog (abridged):

  @{0} 005e8fff  pull origin main: Fast-forward         ← CURRENT = latest upstream
  @{1} a2d2cd47  reset: moving to origin/main
  @{2} 7efe8d15  commit: integrate Mono into latest upstream   ← PRIOR ATTEMPT (base a2d2cd47)
  @{8} a2d2cd47  reset: moving to origin/main
  @{9} 1a315484  commit: ...fix stale Timestamp cache   ← CANONICAL mono tip (base c226e494)
```

- **Canonical mono tip** = `1a315484`, base `c226e494` (old upstream).
- **Prior attempt** = `7efe8d15`, base `a2d2cd47` (recent upstream, includes 5.8-fixes merge).
- **Current tip** = `005e8fff` = `a2d2cd47` + 2 hot-reload commits (`29275c49`, `005e8fff`).

A byte-level comparison shows `7efe8d15` is **identical to `1a315484`** on every Mono-specific file and every key conflict file (CSMonoRuntime.h/.cpp, CSDotNetRuntimeHost_Mono.cpp, PluginLoadContext_Mono.cs, PackageProjectMono.cs, MonoProjectSettings.cs, UnmanagedCallbacks.cs, ManagedCallbacks.cs, CSDotNetRuntimeHost.cpp, CSHotReloadSubsystem.cpp). The only divergence is `UnrealSharpEditor.cpp` (15 lines — an upstream refactor `GetModulePtr`→`LoadModuleChecked` that `7efe8d15` correctly picked up but `1a315484` predates).

**Conclusion:** `7efe8d15` IS the canonical Mono integration, already rebased onto recent upstream. The entire re-integration reduces to: take `7efe8d15`'s diff and apply it onto `005e8fff`, reconciling only the 2 hot-reload commits that came after `a2d2cd47`.

## Conflict surface (verified, not guessed)

Computed as: files touched by the Mono work ∩ files upstream changed between `c226e494` and `005e8fff`.

**21 files in the intersection**, but almost all are trivial. Classified:

### A. Real conflict — `CSHotReloadSubsystem.cpp` (the only one needing thought)
Upstream's 2 newest commits edit `PerformHotReload()` and `OnStructRebuilt()`:
- `29275c49`: adds `FCSHotReloadUtilities::RefreshStructs(ReloadedTypes);` inside the `if (bDetectedNewManagedType)` block; removes `NewStruct->OnChanged(); FStructureEditorUtils::BroadcastPostChange(NewStruct);` from `OnStructRebuilt`.
- `005e8fff`: moves the `RefreshStructs` call OUT of the `bDetectedNewManagedType` guard into the `if (ReloadedTypes.Num() > 0)` block.

Mono's changes to this file are `#if UNREALSHARP_MONO` / `#if !UNREALSHARP_MONO` guards around: `LoadSolutionAsync` (Initialize), `RecompileDirtyProjects`→`BuildUserSolution` (PerformHotReload, ~line 139), `ForceManagedGC` (OnStopPlayingPIE), and `CollectDirtiedFiles`/`ApplyDirtiedFiles` (HandleScriptFileChanges). The `RefreshStructs` / `OnStructRebuilt` area is in **shared (unguarded) code**, so the merge is additive.

**Resolution:** Keep Mono's guards verbatim; accept upstream's `RefreshStructs` relocation and `OnStructRebuilt` body trim in the shared region. These do not overlap Mono's guard boundaries. Expect a clean 3-way merge or a 1–2 hunk manual fix.

### B. Trivial include-ordering — Export headers/sources (~12 files)
`FCSManagerExporter.cpp`, `UGameInstanceExporter.cpp`, `ULocalPlayerExporter.cpp`, `UWorldExporter.cpp/.h`, `FSubsystemCollectionBaseRefExporter.h`, `UDataTableExporter.h`, `UInputComponentExporter.h`, `CSMovementComponentExtensions.h`, `CSTextExtensions.h`, `CSEngineSubsystem.h`, `CSGameInstanceSubsystem.h`, `CSLocalPlayerSubsystem.h/.cpp`, `SubsystemCollectionBaseRef.h`, `CSBlueprint.h`, `CSScriptStruct.h`, `UnrealSharpCore.h`, `CSActorExtensions.cpp`, `CSObjectExtensions.cpp`, `CSPlayerControllerExtensions.cpp`, `CSWorldExtensions.cpp`, `CSManagedClassCompiler.cpp`.

The patch adds `#include` lines for forward-declared UE types (so the file compiles under UE 5.8 / 6 where include discipline tightened). Upstream may have touched nearby lines. These resolve mechanically — keep both sets of includes; ordering is irrelevant for C++ preprocessor.

### C. Whitespace-only — `CSProjectUtilities.cpp`
Patch only changes trailing whitespace; upstream changed the file. Take upstream's version; re-apply the trivial whitespace if desired (or skip — functionally irrelevant).

### D. Managed C# / GlueGenerator — clean apply expected but verify
`UnmanagedCallbacks.cs`, `ManagedCallbacks.cs`, `UnrealFunctionBase.cs`, `StringBuilderExtensions.cs` (GlueGenerator), `GeneratorStringBuilderUtilities.cs`, `Plugin.cs`, `PluginLoader.cs`. These are in the intersection because upstream touched them, but `7efe8d15` already reconciled the `c226e494..a2d2cd47` delta. The remaining `a2d2cd47..005e8fff` delta does NOT touch these files (only the 2 hot-reload C++ commits landed), so they apply cleanly from `7efe8d15`.

## Recommended strategy: cherry-pick `7efe8d15` (no-commit)

> **⚠ SUPERSEDED — see "As-built" below.** The cherry-pick was attempted and abandoned: `7efe8d15` carries stale 5.8 reverts that propagate through the 3-way merge. Final method was `git merge 1a315484`. This section retained for the design rationale.

```
# In submodule Plugins/UnrealSharp, on branch mono202606 (= 005e8fff, clean)
git cherry-pick --no-commit 7efe8d15
#   → 3-way merge; conflicts expected only in CSHotReloadSubsystem.cpp
#   → --no-commit honors "不要提交": leaves the result staged but uncommitted
```

Why cherry-pick (not `git apply`):
- `7efe8d15`'s parent `a2d2cd47` is an ancestor of `005e8fff`, so cherry-pick performs a true 3-way merge using real blob hashes — far more robust than a blind patch.
- It re-adds the MonoSDK gitlink and `.gitmodules` in one step.
- `--no-commit` satisfies the "don't commit" constraint.

Fallback if cherry-pick misbehaves: `git diff a2d2cd47 7efe8d15 | git apply --3way --intent-to-add`.

### As-built: why cherry-pick failed and merge won

**Cherry-pick flaw:** `7efe8d15` is literally the `1a315484` *tree* (mono, base `c226e494` = pre-5.8) replayed onto `a2d2cd47`. Its diff against `a2d2cd47` therefore contains, for every file mono touched, the *full pre-5.8 → mono* delta — including the **revert of every 5.8 refactor** in those files. The cherry-pick 3-way merge (base `a2d2cd47`) saw `theirs`≠base and `ours`=`005e8fff`=`a2d2cd47`+2commits, so it took `theirs` and silently reverted 5.8's `Plugin.cs`/`PluginLoader.cs` rewrite, `CSHotReloadSubsystem.cpp` TRACE/RefreshStructs changes, etc. A `git diff 005e8fff` exposed it (TRACE=0 vs 3, `AddModuleInterfaceInit` restored).

**Patch flaw:** `git apply --3way` of the standalone patch (`c226e494..1a315484`, excluding dead Export dirs) applied, but the patch is only mono's *last leg* — the core C++ integration (`CSManagedCallbacksCache.h` `LookupManagedMethod`, `CSManagedAssembly`) already existed at `c226e494` and is NOT in the patch. Applying it onto clean `005e8fff` would leave C# using `LookupManagedMethod` while C++ uses `GetManagedMethod` — a build/runtime mismatch. Also `git apply` is all-or-nothing and aborted on the 5.8-deleted Export files.

**Merge solution:** `git merge --no-commit --no-ff 1a315484`. Merge base `9dee04c8` (pre-5.8). 5.8's single-sided refactors (`ours`) are auto-taken; mono's single-sided changes (`theirs`, including the core C++) are auto-taken; conflicts arise only where both touched the same lines. Result: 5.8 structure preserved + full mono integration + core C++ included. 13 conflicts, all resolved (see tasks.md "As-built status").

## MonoSDK submodule pointer

| Ref | MonoSDK commit | Notes |
|-----|----------------|-------|
| `7efe8d15` (prior attempt) | `4a6bc8a7` | "Merge ci/auto-build: fix Windows /dev/null -> nul" — **already checked out in working tree** |
| `1a315484` (canonical) | `7446733a` | one commit ahead of `4a6bc8a7` |
| Working tree now | `4a6bc8a7` | matches `7efe8d15` |

Cherry-picking `7efe8d15` records MonoSDK at `4a6bc8a7`, which matches the working tree — no submodule checkout churn. This is the recommended choice. (If the newer `7446733a` is desired, `git -C Source/ThirdParty/MonoSDK checkout 7446733a` and stage the pointer — optional, deferred.)

**Pre-req:** MonoSDK binaries must be present in `Source/ThirdParty/MonoSDK/{Win64,Android,...}/runtime/`. The submodule repo no longer ships binaries (CI-built). Verify `Win64/runtime/` contains `coreclr.dll` + BCL DLLs before building. If absent, fetch from GitHub Releases (see `README_MONO.md`).

## What is NOT in scope

- No new Mono features. No debugger support (the open TODO from prior integration).
- No re-derivation of the 4 editor-crash root causes — they are encoded in the integration itself (`#if WITH_EDITOR && !UNREALSHARP_MONO`, `DefineConstants` fix, UHT `UseMonoRuntime` injection, `mono_runtime_invoke` arg passing). They carry over as-is from `7efe8d15`.
- No fixing the pre-existing Win64-packaged BCL `DllImport` issue (tracked in `mono-integration-status` memory as an open item) unless it blocks verification.
- Outer `SharpDemo` repo stays on `main`; only the submodule branch `mono202606` carries the work. The outer repo's submodule pointer update is also left uncommitted per the constraint.

## Verification plan

1. **Editor build (Mono, Win64):** `Config/DefaultEngine.ini` `[UnrealSharp] bUseMono=true`; build `SharpDemoEditor` Win64 Development. Confirm `[Mono] InitializeMonoHost completed successfully.` in log; editor launches; a C# plugin loads.
2. **Editor build (CoreCLR regression):** set `bUseMono=false`, rebuild editor — confirm CoreCLR path still works (the `#if !UNREALSHARP_MONO` branches).
3. **Win64 packaged (Mono):** `dotnet publish` with `-p:UseMonoRuntime=true --no-self-contained` → UAT BuildCookRun. Confirm PAK contents and BCL NonUFS staging.
4. **Android packaged (arm64, Mono):** BuildAndroid skill flow. Confirm launch on device (joystick etc., per prior verification).
5. **Hot-reload (Mono editor):** edit a C# file, trigger reload — confirm `dotnet build` path runs and no SIGSEGV from the Roslyn backend being skipped.
