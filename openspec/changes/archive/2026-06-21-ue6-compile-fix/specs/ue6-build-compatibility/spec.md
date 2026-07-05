## ADDED Requirements

### Requirement: UnrealSharp UBT plugin targets net10.0 under UE6
The UnrealSharp UBT plugin (`UnrealSharpManagedGlue.ubtplugin`) and its Automation tooling SHALL compile against `net10.0` when building against Unreal Engine 6.0, matching UE6 UBT's runtime target framework. Engine-version detection SHALL resolve to `net10.0` for UE 5.8 and later, and SHALL NOT silently fall back to `net8.0` for UE6.

#### Scenario: UE6 build selects net10.0
- **WHEN** `SharpDemoEditor` is built against `D:\Code\UE6` (engine 6.0.0)
- **THEN** UBT compiles the `UnrealSharpManagedGlue` UBT plugin for `net10.0`
- **AND** UBT does NOT print `Skipping net8.0 project ...UnrealSharpManagedGlue.ubtplugin.csproj`

#### Scenario: UE5.8 build still selects net10.0
- **WHEN** the same plugin is built against an UE 5.8+ engine
- **THEN** the target framework resolves to `net10.0` (no regression of the existing `UE_5_8_OR_LATER` behavior)

### Requirement: No stale UE5 plugin binaries loaded under UE6
The build SHALL NOT load a precompiled UE5/`net8.0` version of the UnrealSharp UBT plugin when targeting UE6. Stale `bin`/`obj` output from a prior UE5 build SHALL be cleared so UBT recompiles the plugin against UE6 assemblies.

#### Scenario: Stale binaries removed before UE6 build
- **WHEN** the project is built under UE6 after applying the fix
- **THEN** no `MissingFieldException` for `LocationOverride.RootDirectory` occurs
- **AND** the UBT plugin loaded at runtime is the one freshly compiled against UE6's `EpicGames.Build`

### Requirement: UBT plugin conforms to UE6 EpicGames.Build API
The UnrealSharp UBT plugin source SHALL compile cleanly against UE6's `EpicGames.Build` and `UnrealBuildTool` assemblies, accounting for API changes such as `Unreal.LocationOverride.RootDirectory` becoming a property.

#### Scenario: Plugin compiles against UE6 assemblies
- **WHEN** the UBT plugin is recompiled on `net10.0` against `D:\Code\UE6\Engine\Binaries\DotNET\UnrealBuildTool\EpicGames.Build.dll`
- **THEN** compilation succeeds with no `MissingFieldException` and no unresolved-member errors referencing removed/renamed UBT APIs

### Requirement: SharpDemo build progresses past the C# tooling phase
After the .NET tooling is unblocked, `SharpDemoEditor` SHALL progress beyond UBT's C# plugin phase to native C++ compilation. Any remaining failures SHALL be native C++ compile/link errors (which are diagnosable), not opaque UBT Internal Compiler Errors in the C# phase.

#### Scenario: C# phase passes, native phase reached
- **WHEN** `SharpDemoEditor` is built under UE6
- **THEN** UBT completes the UnrealSharp bindings-generator initialization without an Internal Compiler Error
- **AND** the build proceeds to compile native C++ modules

### Requirement: GameplayStateTree plugin dependency is consistent
The `SharpDemo` module's dependency on `GameplayStateTreeModule` SHALL be backed by the `GameplayStateTree` plugin being enabled in `SharpDemo.uproject`, eliminating the UBT "does not list plugin ... as a dependency" warning.

#### Scenario: No missing-plugin warning
- **WHEN** `SharpDemoEditor` is built under UE6
- **THEN** UBT does NOT emit `SharpDemo.uproject does not list plugin 'GameplayStateTree' as a dependency`

### Requirement: SharpDemoEditor links cleanly under UE6
The end-to-end outcome of this change is that `SharpDemoEditor` SHALL compile and link successfully against UE6, producing a runnable editor target.

#### Scenario: Clean editor build
- **WHEN** `Build.bat SharpDemoEditor Win64 Development -Project=SharpDemo.uproject` is run against `D:\Code\UE6`
- **THEN** the build completes with `Result: Success` (or equivalent) and the editor target binary is produced
