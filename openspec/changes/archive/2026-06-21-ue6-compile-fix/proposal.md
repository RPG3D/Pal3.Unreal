## Why

After upgrading the SharpDemo project to Unreal Engine 6.0 (engine at `D:\Code\UE6`), the project no longer compiles. The build dies inside UnrealBuildTool before any C++ is compiled: the UnrealSharp C# bindings generator (loaded as a UBT plugin) throws `System.MissingFieldException - Field not found: 'LocationOverride.RootDirectory'`, an Internal Compiler Error. This blocks all development on the demo project under UE6.

Root cause (confirmed by building): UnrealSharp's .NET build tooling detects the engine version via a `UE_5_8_OR_LATER` constant that UE6 no longer defines, so it falls back to targeting `net8.0`. UE6's UBT only loads `net10.0` UBT plugins, so it skips recompiling the plugin (`Skipping net8.0 project ...UnrealSharpManagedGlue.ubtplugin.csproj`) and instead loads a stale, UE5-compiled DLL. That stale binary references `Unreal.LocationOverride.RootDirectory` as a *field*, but in UE6's `EpicGames.Build` it became a *property* (`{ get; set; }`) — hence the `MissingFieldException` at runtime.

## What Changes

- **Fix engine-version detection** in `Plugins/UnrealSharp/UnrealSharp.Automation.props` so the UBT plugin + Automation tooling target `net10.0` under UE6 (currently keyed on `UE_5_8_OR_LATER`, which UE6 does not define).
- **Clear stale precompiled plugin binaries** (`Source/UnrealSharpManagedGlue/bin`, `obj`) that were built against UE5 and cause the `MissingFieldException` when loaded under UE6.
- **Adapt to UE6 UBT / `EpicGames.Build` API changes** surfaced once the plugin recompiles cleanly on `net10.0` (the `LocationOverride.RootDirectory` field→property change is the first; an audit + iterative fix is expected as further breakages compile-fail).
- **Resolve the `GameplayStateTree` plugin dependency**: `SharpDemo.Build.cs` depends on `GameplayStateTreeModule` but the `.uproject` does not enable the `GameplayStateTree` plugin (UBT warning). Add the plugin to the `.uproject` or drop the dependency.
- **Iterate on C++ compile errors** in UnrealSharp's native modules and the demo module that are currently unreachable because UBT dies in the C# phase. These only become visible after the tooling unblocks; the change tracks them as follow-up tasks.

## Capabilities

### New Capabilities
- `ue6-build-compatibility`: The SharpDemo project (game module + UnrealSharp plugin) compiles and generates project files cleanly against Unreal Engine 6.0, covering .NET tooling target-framework detection, UBT/`EpicGames.Build` API conformance, stale-binary hygiene, and native C++ compatibility.

### Modified Capabilities
<!-- None — no prior specs exist in openspec/specs/. -->

## Impact

- **Code**: `Plugins/UnrealSharp/UnrealSharp.Automation.props` (version detection / target framework), `Plugins/UnrealSharp/Source/UnrealSharpManagedGlue/**` (UBT plugin API conformance, bin/obj cleanup), `Plugins/UnrealSharp/Build/Scripts/UnrealSharp.Automation.csproj`, `SharpDemo.uproject` and/or `Source/SharpDemo/SharpDemo.Build.cs` (GameplayStateTree dependency), and UnrealSharp native C++ modules as breakages surface.
- **Submodule**: `Plugins/UnrealSharp` is a git submodule (branch `AndroidClr` / `mono202606`, remote `RPG3D/UnrealSharp`). Plugin fixes are committed inside the submodule, then the parent repo updates the submodule pointer.
- **Dependencies / toolchain**: UE6 ships .NET 10 SDK and MSVC 14.51; UBT runs on `net10.0`. The .NET 8 targeting of the plugin tooling is the incompatibility.
- **Risk**: UE6 is a major version; once the C# tooling unblocks, additional C++ API breakages in UnrealSharp's 9 native modules are likely and will be resolved iteratively. Scope of this change is "unblock and reach a clean compile," not a full UE6 feature audit.
