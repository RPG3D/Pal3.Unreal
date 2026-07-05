# pal3-core-module

Pal3.Core compiles and runs as a UnrealSharp managed module targeting `net10.0`, with no modifications to the original source files.

## Requirements

### Requirement: Pal3.Core compiles as a UnrealSharp managed module
The 292 source files from `E:\Code\Pal3.Unity\Assets\Scripts\Pal3.Core\` SHALL be copied into `Script/Pal3.Core/` preserving their original directory layout and namespace convention (`Pal3.Core.*`). The `.csproj` SHALL target `net10.0`, import `UnrealSharp.Shared.props` for MSBuild consistency, and SHALL NOT reference any Unity, UE, or UnrealSharp assemblies. The module SHALL compile cleanly with zero source modifications to the original `.cs` files.

#### Scenario: Pal3.Core compiles cleanly
- **WHEN** `dotnet build Script/Pal3.Core/Pal3.Core.csproj` is run
- **THEN** the build succeeds with zero errors and zero warnings
- **AND** the output assembly contains all Pal3.Core namespaces: `Pal3.Core.Primitives`, `Pal3.Core.Command`, `Pal3.Core.Command.SceCommands`, `Pal3.Core.DataReader`, `Pal3.Core.FileSystem`, `Pal3.Core.Algorithm`, `Pal3.Core.Contract`, `Pal3.Core.Utilities`, `Pal3.Core.Extensions`

#### Scenario: GameBox math types are self-contained
- **WHEN** `GameBoxVector3`, `GameBoxQuaternion`, `GameBoxMatrix4x4`, `GameBoxRect`, `Color32` are used in C# code
- **THEN** they function identically to the original Pal3.Unity implementation
- **AND** they have zero references to `UnrealSharp`, `UnrealSharp.Core`, `UnrealSharp.CoreUObject`, or any UE type

#### Scenario: Pal3.Core has no engine dependency
- **WHEN** the compiled `Pal3.Core.dll` is inspected with `dotnet list package` or assembly reference analysis
- **THEN** it has zero references to `UnrealSharp*.dll`, `UnityEngine.dll`, `UnityEditor.dll`, or any engine-specific assembly
- **AND** the only framework reference is `net10.0` / `System.Runtime`
