## Context

SharpDemo is an Unreal Engine project that uses the **UnrealSharp** plugin (git submodule at `Plugins/UnrealSharp`, branch `AndroidClr`/`mono202606`) to author C# gameplay. The project was recently repointed at a local UE6 install (`D:\Code\UE6`, version 6.0.0). Building `SharpDemoEditor` now fails before any C++ is compiled.

Confirmed failure (from `Build.bat SharpDemoEditor Win64 Development`):

```
Skipping net8.0 project ...UnrealSharpManagedGlue.ubtplugin.csproj
[UnrealSharp] Initializing UnrealSharp C# bindings generator...
Internal Compiler Error: System.MissingFieldException - Field not found: 'LocationOverride.RootDirectory'.
Unhandled 1 aggregate exceptions
Result: Failed (OtherCompilationError)
```

Mechanism:
- UE6's UBT runs on **.NET 10** and only loads UBT plugins compiled for `net10.0`.
- `Plugins/UnrealSharp/UnrealSharp.Automation.props` selects the target framework by checking whether the engine's `UE5Rules.csproj` defines `UE_5_8_OR_LATER`. UE6 does **not** define that constant (it is no longer "UE5"), so the props fall back to `<TargetFramework>net8.0</TargetFramework>`.
- With `net8.0`, UBT prints `Skipping net8.0 project` and does **not** recompile the `UnrealSharpManagedGlue` UBT plugin. It then loads a **stale, UE5-compiled DLL** already present in `Source/UnrealSharpManagedGlue/bin`.
- That stale DLL references `Unreal.LocationOverride.RootDirectory` as a **field**. In UE6's `EpicGames.Build/Unreal.cs` it is a **property** (`public static DirectoryReference? RootDirectory { get; set; }`). The .NET runtime throws `MissingFieldException` on first access → UBT Internal Compiler Error.

Secondary issue (warning, not yet fatal): `Source/SharpDemo/SharpDemo.Build.cs` lists `GameplayStateTreeModule` as a private dependency, but `SharpDemo.uproject` does not enable the `GameplayStateTree` plugin.

Constraint: The build cannot progress past the C# tooling phase, so any native C++ breakages in UnrealSharp's 9 modules are currently invisible and will only surface iteratively after the tooling is unblocked.

Stakeholders: demo project owner (this repo); UnrealSharp plugin maintainer (submodule remote `RPG3D/UnrealSharp`).

## Goals / Non-Goals

**Goals:**
- Unblock the UE6 build: UBT must compile and load the UnrealSharp UBT plugin on `net10.0` without `MissingFieldException`.
- `SharpDemoEditor` reaches a clean compile (or, if native C++ breakages remain, surfaces them as concrete, fixable errors rather than an opaque ICE in the C# phase).
- Make engine-version detection robust for UE6 (and not regress UE5.8).
- Resolve the `GameplayStateTree` plugin/dependency mismatch.

**Non-Goals:**
- Full UE6 feature/behavior audit of UnrealSharp (e.g. new UE6 gameplay APIs, deprecations). Scope is "compiles cleanly," not "uses UE6 best practices."
- Porting the Android CoreCLR host to UE6 (separate concern; tracked in `android-coreclr-status`).
- Upgrading the demo's C# script project beyond what is needed to compile.
- Changing the UnrealSharp submodule's upstream branching strategy.

## Decisions

### Decision 1: Detect UE6 by engine major version, not by the `UE_5_8_OR_LATER` constant
**Choice:** Replace the `UE_5_8_OR_LATER`-based target-framework selection in `UnrealSharp.Automation.props` with a check that resolves to `net10.0` for UE 5.8 **and** UE6+. Concretely, key off the engine's major version (read from `Engine/Build/Build.version` or the `UE5Rules` define set including `UE_6_0_OR_LATER` if present), defaulting to `net10.0` for any engine ≥ 5.8.

**Why:** The `UE_5_8_OR_LATER` name is engine-internal and UE6 stops defining it; relying on it silently downgrades the tooling to `net8.0`. A version-floor comparison (`>= 5.8 → net10.0`) is stable across future UE versions.

**Alternatives considered:**
- *Hardcode `net10.0` unconditionally.* Simplest, but drops UE5.0–5.7 support that the plugin may still want. Rejected unless the plugin has already dropped <5.8 support (to verify during implementation).
- *Add a sibling `UE_6_0_OR_LATER` constant to the condition.* Minimal diff, but fragile — repeats the original mistake at the next major version.

### Decision 2: Delete stale `bin`/`obj` for the UBT plugin as part of the fix
**Choice:** Clear `Plugins/UnrealSharp/Source/UnrealSharpManagedGlue/bin` and `obj` (and any other stale UE5 plugin output) so UBT is forced to recompile against UE6 assemblies rather than loading a stale DLL.

**Why:** Even after retargeting to `net10.0`, a leftover `net8.0`/UE5 DLL can be picked up and reproduce the `MissingFieldException`. Clean rebuild removes ambiguity.

**Alternatives considered:** *Rely on UBT's incremental detection.* Rejected — UBT explicitly "skips" rather than rebuilds when the TFM mismatches, which is exactly the trap we hit.

### Decision 3: Fix `LocationOverride.RootDirectory` access to be property-compatible
**Choice:** `Build/Scripts/Utilities/PathUtilities.cs:14` already uses assignment (`Unreal.LocationOverride.RootDirectory = ...`), which works for both field and property. No source change needed *there*. The actual `MissingFieldException` comes from the stale binary (Decision 2), not from current source. After retargeting + clean rebuild, this access compiles fine against UE6. If any other source references UBT/`EpicGames.Build` members that changed field↔property or were renamed, fix them as they surface during the clean recompile.

**Why:** Avoid speculative edits; let the `net10.0` recompile against UE6 assemblies report the real, current set of API mismatches.

### Decision 4: Resolve `GameplayStateTree` mismatch by enabling the plugin in `.uproject`
**Choice:** Add `GameplayStateTree` to the `Plugins` array in `SharpDemo.uproject` (Editor target allow-list), keeping the `GameplayStateTreeModule` dependency in `SharpDemo.Build.cs`.

**Why:** The demo's `USharpInputHelper` / state-tree usage relies on that module; removing the dependency is more disruptive than enabling the plugin. `GameplayStateTree` ships with UE6.

**Alternatives considered:** *Remove `GameplayStateTreeModule` from `SharpDemo.Build.cs`.* Rejected unless the demo no longer uses StateTree — to confirm during implementation.

### Decision 5: Plugin fixes land in the submodule, parent repo bumps the pointer
**Choice:** All UnrealSharp edits are committed inside the `Plugins/UnrealSharp` submodule (branch `AndroidClr`/`mono202606`). The parent SharpDemo repo then records the new submodule commit.

**Why:** UnrealSharp is a versioned submodule; editing it in place without committing would leave the parent build non-reproducible. The user owns the `RPG3D/UnrealSharp` remote.

### Decision 6: Stage the work — unblock C# tooling first, then iterate native C++
**Choice:** Tasks are ordered so the .NET tooling (props, stale binaries, API conformance) is fixed and verified first. Only then does the build reach native C++ compilation, at which point UnrealSharp's native modules are compiled and any UE6 C++ API breakages are captured as concrete errors and fixed iteratively.

**Why:** The current ICE hides everything downstream. There is no way to enumerate native breakages without first getting past UBT.

## Risks / Trade-offs

- **[Hidden native C++ breakages in UnrealSharp's 9 modules]** → Expected. After unblock, treat each compile error as a follow-up task; do not declare the change done until `SharpDemoEditor` links cleanly. Time-box discovery.
- **[Version-detection logic mis-reads UE6 `Build.version`]** → Mitigation: log the detected major/minor version during the build; fall back to `net10.0` when detection is uncertain (UE6 requires it).
- **[Stale binaries regenerated from a cached `obj`]** → Mitigation: clear both `bin` and `obj`; verify the recompiled DLL targets `net10.0` (inspect with `dotnet`/file properties) before re-running UBT.
- **[Other `EpicGames.Build` / UBT API removals beyond `LocationOverride.RootDirectory`]** → Mitigation: do a clean `net10.0` recompile and fix each compiler error; optionally pre-audit with a grep for UBT internal APIs used by the plugin.
- **[UE6 MSVC 14.51 "newer than preferred" warning]** → Noted by UBT but not a failure; no action unless it produces errors.
- **[Submodule branch divergence]** → Mitigation: commit plugin fixes to the existing `AndroidClr`/`mono202606` branch the parent repo already tracks; do not rebranch.
- **[Scope creep into full UE6 migration]** → Mitigation: Non-Goals explicitly bound this to "compiles cleanly"; feature work is deferred.

## Migration Plan

1. In the submodule, update `UnrealSharp.Automation.props` version detection → `net10.0` for UE ≥ 5.8.
2. Delete stale `UnrealSharpManagedGlue/bin` + `obj`.
3. Rebuild `SharpDemoEditor`; confirm UBT recompiles the plugin on `net10.0` and the `MissingFieldException` is gone.
4. Fix any further UBT/`EpicGames.Build` C# compile errors surfaced.
5. Add `GameplayStateTree` plugin to `SharpDemo.uproject`.
6. Continue building; fix native C++ breakages in UnrealSharp modules iteratively until the editor links.
7. Commit submodule changes; update submodule pointer in parent repo.

**Rollback:** Revert the parent-repo submodule pointer to the previous commit (`68651c15`) and restore `SharpDemo.uproject`. The UE5 build path is unaffected by these changes.

## Open Questions

- Does UnrealSharp still need to support UE < 5.8? If not, Decision 1 simplifies to "always `net10.0`." (Check plugin README / CI matrix during implementation.)
- Does the demo actually still use `GameplayStateTreeModule`? Confirm before committing to Decision 4 vs. removing the dependency.
- Are there known UE6 UBT API migration notes from Epic we should follow for the plugin audit? (Search UE6 `Engine/Source/Programs/UnrealBuildTool` release notes / `Build.version` changelog during implementation.)
