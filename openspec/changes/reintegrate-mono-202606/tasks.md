# Tasks — Re-integrate Mono onto `mono202606`

> All work is in the **submodule** `Plugins/UnrealSharp` on branch `mono202606` (currently `005e8fff`).
> **Do not commit / do not push** — leave results in the working tree.

## 1. Prepare clean working tree

- [ ] 1.1 In `Plugins/UnrealSharp`, confirm `mono202606` is at `005e8fff` and HEAD is clean: `git status` (expect only untracked Mono leftovers + `Source/ThirdParty/MonoSDK`).
- [ ] 1.2 Stash or remove the untracked mono-new-file leftovers (`.gitmodules`, `PackageProjectMono.cs`, `MonoProjectSettings.cs`, `PluginLoadContext_Mono.cs`, `README_MONO.md`, `CSDotNetRuntimeHost_Mono.cpp`, `CSMonoRuntime.cpp/.h`) so they don't collide with the cherry-pick — `git clean -nd Source/ Build/ Managed/` to preview, then move them aside (they will be recreated by the cherry-pick).
- [ ] 1.3 Preserve the existing `Source/ThirdParty/MonoSDK` checkout (`4a6bc8a7`) — do NOT delete it; the cherry-pick re-registers it as a submodule pointing at the same commit.

## 2. Re-apply the Mono integration (no-commit cherry-pick)

- [ ] 2.1 Run `git cherry-pick --no-commit 7efe8d15` in `Plugins/UnrealSharp`.
- [ ] 2.2 Expect a conflict in `Source/UnrealSharpEditor/Private/HotReload/CSHotReloadSubsystem.cpp` (and possibly trivial include-ordering in a few Export headers). All other files should apply cleanly.
- [ ] 2.3 If cherry-pick refuses due to the untracked-file leftovers, use the fallback: `git cherry-pick --abort; git diff a2d2cd47 7efe8d15 | git apply --3way --intent-to-add`.

## 3. Resolve conflicts

- [ ] 3.1 **`CSHotReloadSubsystem.cpp`** — keep Mono's `#if UNREALSHARP_MONO` / `#if !UNREALSHARP_MONO` guards intact; accept upstream's two changes in the shared (unguarded) region:
  - In `PerformHotReload()`: `FCSHotReloadUtilities::RefreshStructs(ReloadedTypes);` lives inside the `if (ReloadedTypes.Num() > 0)` block (NOT inside `if (bDetectedNewManagedType)`), per `005e8fff`.
  - In `OnStructRebuilt(UCSScriptStruct*)`: body is just `AddReloadedType(NewStruct);` — the `NewStruct->OnChanged();` and `FStructureEditorUtils::BroadcastPostChange(NewStruct);` lines are removed, per `29275c49`.
- [ ] 3.2 **Export headers/sources** (group B in design) — for any `#include`-ordering conflict, keep both Mono's added includes and upstream's; C++ include order is not significant. Verify each conflicted file still includes the types it forward-declares.
- [ ] 3.3 **`CSProjectUtilities.cpp`** — take upstream's version; the patch's change here is whitespace-only and irrelevant.
- [ ] 3.4 `git add` all resolved files. Confirm `git status` shows no remaining conflicts (changes staged, uncommitted).

## 4. Restore MonoSDK submodule registration

- [ ] 4.1 Confirm `.gitmodules` now contains the `Source/ThirdParty/MonoSDK` entry (recreated by cherry-pick).
- [ ] 4.2 Confirm `git ls-files -s Source/ThirdParty/MonoSDK` shows `160000 4a6bc8a7...` (gitlink).
- [ ] 4.3 Confirm `Source/ThirdParty/MonoSDK/Win64/runtime/` contains `coreclr.dll` + BCL DLLs. If missing, download from https://github.com/RPG3D/MonoSDK/releases and extract into `Source/ThirdParty/MonoSDK/`.
- [ ] 4.4 (Optional) Bump MonoSDK to `7446733a` (canonical pointer): `git -C Source/ThirdParty/MonoSDK checkout 7446733a && git add Source/ThirdParty/MonoSDK`. Defer if `4a6bc8a7` builds fine.

## 5. Config + outer repo

- [ ] 5.1 Ensure `Config/DefaultEngine.ini` has `[UnrealSharp]` `bUseMono=true` (to exercise Mono path during editor build verification).
- [ ] 5.2 Note: the outer `SharpDemo` repo will show `M Plugins/UnrealSharp` (modified submodule) — leave uncommitted per the constraint.

## 6. Verify — Editor (Mono, Win64)

- [ ] 6.1 Build `SharpDemoEditor` Win64 Development (BuildEditor skill) with `bUseMono=true`.
- [ ] 6.2 Confirm build succeeds; `UNREALSHARP_MONO=1` defined in `UnrealSharpCore` (MonoSDK.Build.cs).
- [ ] 6.3 Launch editor (RunEditor skill); confirm log shows `[Mono] InitializeMonoHost completed successfully.` and a C# plugin loads without `/tmp/UnrealSharp_InitException.txt`.

## 7. Verify — CoreCLR regression (Win64 editor)

- [ ] 7.1 Set `bUseMono=false`, rebuild editor, confirm CoreCLR path still initializes (the `#if !UNREALSHARP_MONO` branches compile and run).
- [ ] 7.2 Restore `bUseMono=true` after.

## 8. Verify — Packaged (Win64 + Android, Mono)

- [ ] 8.1 Win64: `dotnet publish` bindings + user scripts with `-p:UseMonoRuntime=true --no-self-contained` → UAT BuildCookRun (BuildWin64 skill). Confirm PAK contains project DLLs + LoadOrder.json and BCL is staged NonUFS outside PAK.
- [ ] 8.2 Android arm64: BuildAndroid skill flow. Confirm package builds and (if device available) launches on device.
- [ ] 8.3 Note any pre-existing Win64 BCL `DllImport` failure (`System.Globalization.Native.dll`) — record but do not block on it (open item from prior integration).

## 9. Verify — Hot-reload (Mono editor)

- [ ] 9.1 With `bUseMono=true` editor running, edit a user C# file, trigger hot reload.
- [ ] 9.2 Confirm the `dotnet build` path runs (Mono branch in `PerformHotReload`) and no SIGSEGV from the skipped Roslyn `UnrealSharp.Editor.dll` / `LoadSolutionAsync`.

## 10. Hand-off

- [ ] 10.1 Summarize applied changes, conflicts resolved, and verification results to user.
- [ ] 10.2 Leave everything uncommitted in the working tree for user review (do NOT `git commit` / `git push`).
- [ ] 10.3 Update `mono-integration-status` memory with the new branch state once verified.

---

## As-built status (2026-06-28)

### Approach pivoted: merge, not cherry-pick

The cherry-pick plan (§2) was abandoned mid-execution. `7efe8d15` is the `1a315484` tree rebased onto `a2d2cd47`, so cherry-picking it onto `005e8fff` **silently reverted all 5.8-era upstream refactors** in files mono also touched (e.g. `Plugin.cs`/`PluginLoader.cs` `AddModuleInterfaceInit`/`FindModule<T>` reverted to the old `Activator`/`GetTypes` pattern; `CSHotReloadSubsystem.cpp` lost its `TRACE_CPUPROFILER` + `RefreshStructs` relocation). A `git diff` against `005e8fff` exposed the stale reverts.

**Final method:** `git merge --no-commit --no-ff 1a315484` in the submodule. Merge base is `9dee04c8` (pre-5.8 upstream), so 5.8's refactors (`ours`, single-sided) are preserved while mono's changes apply on top — conflicts only where both sides touched the same region. This also pulls in the **core C++ integration** (`CSManagedCallbacksCache.h`, `CSManagedAssembly`) that the standalone patch missed entirely (the patch was only mono's *last leg* on top of an already-mono `c226e494`).

### Conflicts resolved (13 total)

- **7 modify/delete (Export/ dir):** 5.8 deleted the entire `Source/UnrealSharpCore/{Private,Public}/Export/` directory; mono had only added `#include`s there. Verified no external references → `git rm` (take 005e8fff deletion).
- **`CSScriptStruct.h`:** take `ours` (5.8 dropped the obsolete `#if ENGINE_MINOR_VERSION >= 5` guard; always uses `StructUtils/UserDefinedStruct.h`).
- **`CSHotReloadSubsystem.cpp`:** auto-merged all 5 `#if UNREALSHARP_MONO` guards; only a 1-line blank-line conflict — resolved, full 5.8 structure (TRACE=3, `RefreshStructs` in `ReloadedTypes.Num()>0` block, `OnStructRebuilt` body trim) preserved + mono guards intact.
- **`ManagedCallbacks.cs`:** take `ours` (`GetManagedMethod`/`GetManagedTypeHandle`, `char*`) — C++ `CSManagedCallbacksCache.h` (5.8) uses `GetManagedMethod`, so C# must match, NOT mono's `LookupManagedMethod` rename.
- **`UnmanagedCallbacks.cs`:** keep `ours` signatures (`GetManagedMethod`/`GetManagedTypeHandle`, `char*`), keep mono's `#if UNREALSHARP_MONO` dual-path bodies, adapt `methodNamePtr`/`fullTypeNamePtr` → `methodName`/`fullTypeName`.
- **`Plugin.cs`:** keep BOTH `ours` `AddModuleInterfaceInit` (5.8) AND `theirs` `CreateLoadContext` (mono ALC factory).
- **`PluginLoader.cs`:** hand-rewrote to 5.8 structure (`Dictionary Plugins`, `FindPlugin`, `FindModule<T>`) + mono's two guards (`LoadPlugin` exception temp-file path; `UnloadPlugin` async Mono early-return).

### 5.8 API adaptation (beyond conflicts)

`CSDotNetRuntimeHost_Mono.cpp` referenced pre-5.8 symbols. Fixed (this was the one compile error):
- `#include "CSBindsManager.h"` → `#include "CSBindsRegistry.h"` (5.8 rename)
- `FCSBindsManager::GetBoundFunction` → `FCSBindsRegistry::GetBoundFunction`

### Compile verification (DONE — static)

- [x] **Managed bindings** (`Managed/UnrealSharp/UnrealSharp.sln`, `-p:UseMonoRuntime=true`): 0 errors, 819 pre-existing warnings.
- [x] **User scripts** (`Script/ManagedSharpDemo.sln`, `-p:UseMonoRuntime=true`): 0 errors, 7 pre-existing warnings.
- [x] **Editor C++ (Mono, Win64, `bUseMono=true`):** `SharpDemoEditor` build **SUCCEEDED**. `UNREALSHARP_MONO=1`, MonoSDK linked, `coreclr.dll` staged to `Binaries/Win64/`.
- [x] **CoreCLR regression (`bUseMono=false`):** rebuild **SUCCEEDED** — `#if !UNREALSHARP_MONO` hostfxr path compiles. Restored `bUseMono=true` + final Mono rebuild so the binary matches the runtime config.

### Runtime verification (PENDING — tomorrow)

- [ ] 8 / 9: Editor PIE launch (`[Mono] InitializeMonoHost completed successfully.`), Mono hot-reload, packaged Win64 + Android arm64.
- [ ] Note: pre-existing open item — Win64 packaged BCL `DllImport` of `System.Globalization.Native.dll` (from prior integration memory) may resurface; record but don't block.

### Final state

- Submodule `Plugins/UnrealSharp` @ `005e8fff`, **merge in progress, uncommitted** (per "不要提交"). 53 files staged = full mono integration + BindsRegistry adaptation. MonoSDK gitlink @ `4a6bc8a7` (working tree, binaries present: 261 Win64 runtime DLLs). `bUseMono=true`. No `.claude` contamination.
- To capture: `cd Plugins/UnrealSharp && git commit` (concludes the merge). Outer `SharpDemo` repo will then show `M Plugins/UnrealSharp` for its pointer update.

