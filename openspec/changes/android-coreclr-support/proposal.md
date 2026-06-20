## Why

UnrealSharp today only runs C# on desktop platforms — `CSDotNetRuntimeHost` initializes CoreCLR exclusively through `hostfxr`, and the `.uplugin` declares `SupportedTargetPlatforms: Win64, Mac`. Android is not supported. `hostfxr` does not exist on Android, so bringing C# scripting to Android requires a different runtime bootstrap. We have already proven on a standalone demo (`E:\Code\CoreClrDemo\AndroidClrDemo`) that the raw `coreclr_initialize` / `coreclr_create_delegate` C API can host CoreCLR and execute a managed DLL inside an Android process. This change ports that approach into UnrealSharp so the same game scripts authored for desktop run on Android (arm64) — the managed IL is platform-agnostic.

## What Changes

- Add an Android runtime-host path based on the raw `coreclrhost.h` C API (`coreclr_initialize`, `coreclr_create_delegate`, `coreclr_set_error_writer`) alongside the existing desktop `hostfxr` path. The host dispatches by platform with a plain `#if PLATFORM_ANDROID` — Android uses the raw API, Win64/Mac keep `hostfxr`. **No opt-in switch, no Mono-style macro**: Android is a first-class supported platform, dispatched the same way Win64/Mac are, consistent with the plugin's existing per-platform handling.
- Add the `coreclrhost.h` raw-API header (raw `coreclr_initialize`/`coreclr_create_delegate`/`coreclr_set_error_writer`/`coreclr_shutdown` declarations, from `dotnet/runtime` `src/coreclr/hosts/inc`) to `Managed/DotNetRuntime/inc` — currently only `hostfxr.h` / `coreclr_delegates.h` / `nethost.h` ship, none of which expose `coreclr_initialize`.
- Build the Trusted Platform Assemblies (TPA) list at runtime by scanning the staged managed-DLL directory, mirroring `build_tpa()` from the demo.
- Extract BCL + project managed DLLs from the PAK to a writable runtime directory (`ProjectPersistentDownloadDir/Managed/Android/`) at first launch, then point TPA / `APP_CONTEXT_BASE_DIRECTORY` / `NATIVE_DLL_SEARCH_DIRECTORIES` there. CoreCLR requires real filesystem paths (unlike Mono's preload hook), so a one-time extraction cache is required.
- Stage CoreCLR native libraries (`libcoreclr.so`, `libclrjit.so`, `libSystem.*.so`) into the APK `lib/arm64-v8a/` via an Android Plugin Language (APL) XML file so the OS linker loads them. Stage BCL managed DLLs as NonUFS (outside PAK, runtime-version-bound) and project DLLs + `LoadOrder.json` into the PAK as UFS (source of truth, hot-update friendly), reusing the staging pattern from the Mono port.
- Provide a `CoreClrSDK` ThirdParty module (populated from a `dotnet publish -r android-arm64 --self-contained`) holding the prebuilt runtime + BCL, plus a packaging command that publishes cross-platform project IL to `Content/Managed/Android/`. The full packaging flow is `BuildManagedCode` (IL) → `UAT BuildCookRun -TargetPlatform=Android`.
- Add `Android` to `SupportedTargetPlatforms` in `UnrealSharp.uplugin`.
- Wire CoreCLR diagnostics to logcat via `coreclr_set_error_writer` (the demo's mechanism — `DOTNET_HOST_TRACE` is hostfxr-only and unavailable on Android).
- **BREAKING** (for Android only, a new platform): none for existing Win64/Mac users; Android is newly enabled.

## Capabilities

### New Capabilities
- `android-coreclr-host`: Bootstrap CoreCLR on Android (arm64) via the raw `coreclr_initialize` C API — TPA construction, runtime-directory extraction from PAK, native-library staging, diagnostics via `coreclr_set_error_writer`, and entry-point delegate invocation.

### Modified Capabilities
<!-- No existing specs in openspec/specs/. The Mono integration was archived and its specs removed; the CoreCLR host abstraction has no prior spec. None to modify. -->

## Impact

- **Code**: `Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost.cpp` + header gain platform dispatch; new Android host implementation; `Managed/DotNetRuntime/inc` gains `coreclr.h`. New `Source/ThirdParty/` CoreCLR runtime staging module (analogous to `MonoSDK.Build.cs`) for `libcoreclr.so` / `libclrjit.so` / `libSystem.*.so` and BCL managed DLLs.
- **Build/packaging**: `Build/Scripts/BuildCommands/` gains Android publish + staging; `UnrealSharp.uplugin` gains `Android`.
- **Dependencies**: Prebuilt Android CoreCLR runtime + BCL for arm64 (mirrors the MonoSDK submodule pattern — a separate `CoreClrSDK` artifact under `Source/ThirdParty/`). Requires .NET SDK with `android` workload + Android NDK for the publish step.
- **Reference materials**: `AndroidClrDemo/README.md` (raw CoreCLR hosting flow, TPA, `coreclr_set_error_writer`, APK layout) and the archived Mono port commit `a6d3380b` (UE packaging pattern: `dotnet publish` → `Content/Managed/{Platform}/`, BCL NonUFS / project DLL UFS via `RuntimeDependencies`, `LoadOrder.json`, `PersistentDownloadDir` override, `PLATFORM_` naming).
- **Out of scope**: iOS (separate AOT/interpreter work), Android x86/arm32, remote debugging on device (documented as future work like the Mono port).
