# mono-runtime-integration

UnrealSharp supports a dual .NET runtime architecture: CoreCLR (`hostfxr`) is the default for desktop editor/packaged builds, while Mono (`libmonosgen-2.0`) is opt-in for platforms where CoreCLR is unavailable (Android, iOS). The runtime is selected at build time via `bUseMono=true` in `[UnrealSharp]` of `Config/DefaultEngine.ini`, which defines `UNREALSHARP_MONO` across all C++ modules and managed assemblies.

## MODIFIED Requirements

### Requirement: Dual runtime selected by bUseMono, no code duplication
The integration SHALL be gated by the `UNREALSHARP_MONO` preprocessor symbol in C++ and the `UNREALSHARP_MONO` C# define, both derived from `bUseMono` in `DefaultEngine.ini`. `MonoSDK.Build.cs` SHALL define `UNREALSHARP_MONO=1` and link the Mono runtime when `bUseMono=true`; otherwise the CoreCLR/hostfxr path SHALL compile and run unchanged. No runtime SHALL be selected at runtime — the choice is fixed at build time.

#### Scenario: CoreCLR default path is unaffected
- **WHEN** `bUseMono` is absent or `false`
- **THEN** `UnrealSharpCore` builds against the CoreCLR/hostfxr headers in `Managed/DotNetRuntime/inc/`
- **AND** `UNREALSHARP_MONO` is `0`
- **AND** `FCSDotNetRuntimeHost::InitializeManagedRuntime()` takes the `#if !UNREALSHARP_MONO` hostfxr path
- **AND** the editor initializes CoreCLR exactly as upstream

#### Scenario: Mono opt-in path
- **WHEN** `[UnrealSharp] bUseMono=true` is set in `DefaultEngine.ini`
- **THEN** `UnrealSharpCore` and `UnrealSharpUtilities` link the `MonoSDK` module and `UNREALSHARP_MONO=1` is defined
- **AND** `InitializeManagedRuntime()` calls `InitializeMonoHost()`
- **AND** the editor startup skips `VerifyCSharpEnvironment()`/CoreCLR-only checks under `#if WITH_EDITOR && !UNREALSHARP_MONO`

### Requirement: Mono host initializes and invokes the C# entry point
`InitializeMonoHost()` SHALL resolve the BCL runtime directory, set UFS-aware assembly search paths, install a `mono_install_assembly_preload_hook` that loads assemblies via `FFileHelper::LoadFileToArray` (pak-aware), initialize the JIT (`mono_jit_init_version`), and invoke `UnrealSharp.Plugins.Main.InitializeUnrealSharp` via `FindMonoMethod` + `mono_runtime_invoke` (NOT `mono_method_get_unmanaged_callers_only_ftnptr`, which crashes on complex pointer params in .NET 10 Mono).

#### Scenario: Editor launches under Mono
- **WHEN** the editor starts with `bUseMono=true` and `UnrealSharp.Plugins.dll` + BCL are present
- **THEN** the log contains `[Mono] InitializeMonoHost completed successfully.`
- **AND** no `/tmp/UnrealSharp_InitException.txt` is written
- **AND** a managed plugin loads and registers managed callbacks

#### Scenario: Assembly preload hook reads from PAK
- **WHEN** a managed assembly is requested that lives inside a PAK (packaged build)
- **THEN** `LoadAssemblyFromUFS` finds it via `FFileHelper::LoadFileToArray` through UE I/O
- **AND** the assembly loads without requiring OS-level filesystem access

### Requirement: Mono hot-reload coexists with upstream hot-reload fixes
Under Mono, hot-reload SHALL use `dotnet build` (via `UnrealSharp::DotNetUtilities::BuildUserSolution`) instead of the Roslyn backend (`UnrealSharp.Editor.dll` is not loaded under Mono). The `#if UNREALSHARP_MONO` guards in `CSHotReloadSubsystem.cpp` SHALL preserve upstream's `RefreshStructs` relocation (inside the `if (ReloadedTypes.Num() > 0)` block) and `OnStructRebuilt` body change from commits `29275c49`/`005e8fff`.

#### Scenario: Mono hot-reload does not crash
- **WHEN** a user C# file is edited and hot-reload is triggered under Mono
- **THEN** `BuildUserSolution()` runs (`dotnet build` path)
- **AND** `LoadSolutionAsync` / `RecompileDirtyProjects` / `ForceManagedGC` (Roslyn-dependent) are skipped via `#if !UNREALSHARP_MONO`
- **AND** no SIGSEGV occurs from a null Roslyn backend

#### Scenario: Upstream hot-reload fix preserved
- **WHEN** a new struct and class are hot-reloaded in the same cycle
- **THEN** `RefreshStructs(ReloadedTypes)` is called inside the `ReloadedTypes.Num() > 0` block
- **AND** `OnStructRebuilt` no longer calls `NewStruct->OnChanged()` / `BroadcastPostChange` (deferred to the refresh path)

### Requirement: Mono packaging stages BCL outside PAK, project DLLs inside PAK
`PackageProjectMono` SHALL publish managed DLLs to `Content/Managed/{Platform}/` with `--no-self-contained` + `-p:UseMonoRuntime=true`, deduplicate BCL DLLs from the publish output, and emit the user load-order manifest. `MonoSDK.Build.cs` SHALL stage BCL as `StagedFileType.NonUFS` (outside PAK) and project DLLs as `StagedFileType.UFS` (inside PAK, hot-updatable).

#### Scenario: Win64 packaged build
- **WHEN** `dotnet publish` + UAT BuildCookRun runs with Mono on Win64
- **THEN** project DLLs + `UserCode.LoadOrder.json` appear inside the PAK
- **AND** BCL DLLs (`coreclr.dll`, `System.Private.CoreLib.dll`, `*Native*.dll`) are staged outside the PAK alongside the executable

#### Scenario: Android arm64 packaged build
- **WHEN** the Android Mono package is built
- **THEN** `libmonosgen-2.0.so` and BCL are staged via APL/RuntimeDependencies
- **AND** the package launches and runs C# scripts on device
