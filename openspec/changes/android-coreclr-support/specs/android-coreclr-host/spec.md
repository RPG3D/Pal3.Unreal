## ADDED Requirements

### Requirement: Android runtime bootstrap via raw CoreCLR C API
On Android (arm64), the runtime host SHALL bootstrap CoreCLR using the raw `coreclrhost.h` C API (`coreclr_initialize`, `coreclr_create_delegate`, `coreclr_set_error_writer`, `coreclr_shutdown`) instead of `hostfxr`. The desktop `hostfxr` path SHALL remain unchanged for Win64/Mac. Both paths SHALL converge on the same managed entry point (`UnrealSharp.Plugins.Main.InitializeUnrealSharp`). Android SHALL be a first-class supported platform dispatched with a plain `#if PLATFORM_ANDROID` — the runtime engine is CoreCLR on every platform; Android simply reaches it through the raw C API because hostfxr is unavailable there.

#### Scenario: Android host uses coreclr_initialize
- **WHEN** the plugin initializes on `PLATFORM_ANDROID`
- **THEN** the host calls `coreclr_initialize` with `TRUSTED_PLATFORM_ASSEMBLIES`, `APP_CONTEXT_BASE_DIRECTORY`, and `NATIVE_DLL_SEARCH_DIRECTORIES` properties, and obtains the `InitializeUnrealSharp` delegate via `coreclr_create_delegate`

#### Scenario: Desktop host unchanged
- **WHEN** the plugin initializes on Win64 or Mac
- **THEN** the host uses the existing `hostfxr` flow and the Android code path is excluded by the preprocessor

#### Scenario: No opt-in switch required
- **WHEN** the project builds or cooks for Android
- **THEN** the Android CoreCLR host and `CoreClrSDK` staging activate unconditionally (no `bUseAndroidCoreClr` ini setting, no `UNREALSHARP_ANDROID_CORECLR` macro), identical in spirit to how Win64/Mac are handled

### Requirement: Trusted Platform Assemblies construction
The Android host SHALL build the TPA list by scanning the runtime managed-DLL directory for `*.dll` and joining absolute paths with the platform path separator, mirroring the demo's `build_tpa()`.

#### Scenario: TPA includes BCL and project assemblies
- **WHEN** `coreclr_initialize` is called
- **THEN** the TPA property contains absolute filesystem paths to every managed DLL (BCL + project) present in the runtime directory, and `coreclr_initialize` returns a valid host handle

#### Scenario: Missing runtime directory
- **WHEN** the runtime managed-DLL directory does not exist or is empty
- **THEN** the host logs an error listing the expected path and aborts initialization with a fatal error

### Requirement: Managed DLL extraction from PAK to writable runtime directory
On Android, the host SHALL ensure a writable runtime directory exists at `ProjectPersistentDownloadDir()/Managed/Android/` and SHALL extract BCL managed DLLs, project managed DLLs, and `LoadOrder.json` there from the PAK (UE I/O) before constructing TPA. The host SHALL NOT use `ProjectSavedDir()` as the runtime directory.

#### Scenario: First-launch extraction
- **WHEN** the runtime directory does not contain the expected managed DLLs
- **THEN** the host extracts them from the PAK into `ProjectPersistentDownloadDir()/Managed/Android/`

#### Scenario: Skip unchanged extraction
- **WHEN** the runtime directory already contains DLLs matching the PAK source (per-file mtime: dest exists and is at least as new as the source)
- **THEN** the host skips re-extraction of those files

#### Scenario: Hot-updated project DLL
- **WHEN** a project DLL in the PAK is newer than the extracted copy
- **THEN** the host re-extracts that DLL so the updated assembly loads on next launch

### Requirement: CoreCLR native library staging
The build system SHALL stage Android CoreCLR native libraries (`libcoreclr.so`, `libclrjit.so`, `libSystem.*.so`) into the APK `lib/arm64-v8a/` via an Android Plugin Language (APL) XML file registered through `AdditionalPropertiesForReceipt("AndroidPlugin", ...)`, so the OS linker loads them at install time. BCL managed DLLs SHALL be staged NonUFS (outside PAK, runtime-version-bound) and project managed DLLs + `LoadOrder.json` SHALL be staged into the PAK as UFS `RuntimeDependencies`, sourced from the current platform's `Content/Managed/Android/` subdir.

#### Scenario: Native libs loadable by dlopen
- **WHEN** the APK is packaged for Android arm64
- **THEN** `libcoreclr.so`, `libclrjit.so`, and `libSystem.*.so` are present under `lib/arm64-v8a/` and resolvable by the dynamic linker at runtime

#### Scenario: Managed DLLs in PAK
- **WHEN** the APK is packaged
- **THEN** BCL managed DLLs are staged NonUFS (outside PAK) and project managed DLLs + `LoadOrder.json` are staged as UFS (PAK-resident), with no BCL duplication between the two

### Requirement: CoreClrSDK runtime artifacts
A `Source/ThirdParty/CoreClrSDK/` directory SHALL hold the prebuilt Android CoreCLR runtime + BCL, populated from a `dotnet publish -r android-arm64 --self-contained`: native `.so` in `Android/lib/`, BCL managed `.dll` in `Android/runtime/`. `CoreClrSDK.Build.cs` SHALL be a no-op on non-Android platforms.

#### Scenario: CoreClrSDK populated for Android
- **WHEN** CoreClrSDK is populated
- **THEN** `Android/lib/` contains `libcoreclr.so`, `libclrjit.so`, and the `libSystem.*.so` interop libs, and `Android/runtime/` contains the BCL managed DLLs including `System.Private.CoreLib.dll`

#### Scenario: Desktop unaffected
- **WHEN** the plugin builds for Win64 or Mac
- **THEN** `CoreClrSDK.Build.cs` performs no staging and defines no Android macros

### Requirement: Android packaging flow
The build system SHALL provide a reproducible Android packaging flow: cross-platform project IL is published to `Content/Managed/Android/` (the runtime + BCL come from `CoreClrSDK`, staged NonUFS/APL), `LoadOrder.json` is placed alongside, and `UAT BuildCookRun -TargetPlatform=Android` stages and packages the APK.

#### Scenario: Project IL published to Android content dir
- **WHEN** the managed build runs
- **THEN** the project + bindings managed DLLs (cross-platform IL) are placed in `Content/Managed/Android/`, with BCL DLLs deduplicated against `CoreClrSDK/Android/runtime/`

#### Scenario: UAT packages Android APK
- **WHEN** `UAT BuildCookRun` runs with `-TargetPlatform=Android`
- **THEN** the resulting APK contains the staged native libs under `lib/arm64-v8a/`, the BCL managed DLLs NonUFS, and the project managed DLLs + `LoadOrder.json` inside the PAK

### Requirement: Android platform support declaration
`UnrealSharp.uplugin` SHALL list `Android` in `SupportedTargetPlatforms`.

#### Scenario: Android is a supported target
- **WHEN** UBT enumerates supported target platforms for the plugin
- **THEN** `Android` is included, enabling the plugin to cook and package for Android

### Requirement: CoreCLR diagnostics on Android
The Android host SHALL register a `coreclr_set_error_writer` callback that forwards CoreCLR diagnostics to `UE_LOG(LogUnrealSharp)` (routed to logcat). The host SHALL NOT rely on `DOTNET_HOST_TRACE` (hostfxr-only, unavailable on Android).

#### Scenario: CoreCLR error captured
- **WHEN** CoreCLR emits an error or warning during initialization or execution
- **THEN** the message appears in logcat via the registered error-writer callback
