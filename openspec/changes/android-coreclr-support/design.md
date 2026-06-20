## Context

UnrealSharp's runtime host (`Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost.cpp`) today uses `hostfxr` exclusively: it `dlopen`s a `hostfxr` library, binds `hostfxr_initialize_for_dotnet_command_line` / `hostfxr_initialize_for_runtime_config` / `hostfxr_get_runtime_delegate` / `hostfxr_close`, obtains the `load_assembly_and_get_function_pointer` delegate, then invokes the `UnrealSharp.Plugins.Main.InitializeUnrealSharp` entry point. The plugin's `SupportedTargetPlatforms` is `Win64, Mac`.

`hostfxr` is a desktop-only host policy layer; it does not ship for Android. The verified `AndroidClrDemo` proves the alternative: link/dlopen `libcoreclr.so` and call the raw C API — `coreclr_initialize` (builds the runtime + default assembly-load context from explicit TPA / base-dir / native-search-dir properties), `coreclr_create_delegate` (obtains a native function pointer to a managed static method), `coreclr_set_error_writer` (captures CoreCLR diagnostics into logcat). The demo unzips all managed DLLs from `assets.zip` to `filesDir` and builds TPA by scanning that directory.

The archived Mono port (commit `a6d3380b`, `README_MONO.md`) solved the UE-packaging half for a mobile runtime and gives reusable patterns: `dotnet publish` project DLLs → `Content/Managed/{Platform}/`; BCL staged NonUFS (outside PAK, runtime-version-bound) and project DLLs staged UFS (inside PAK, hot-update) via a `*.Build.cs` `RuntimeDependencies`; `LoadOrder.json` manifest; `ProjectPersistentDownloadDir()` as the writable runtime/override path on Android (NOT `ProjectSavedDir()`, which returns the SD root on Android); `PLATFORM_`-macro → string platform naming; `coreclr_set_error_writer` for diagnostics.

Constraint: CoreCLR's raw API requires TPA entries to be **real OS filesystem paths**. Unlike Mono's `mono_install_assembly_preload_hook` + `FFileHelper::LoadFileToArray` (which loaded assemblies straight out of the PAK with no extraction), CoreCLR cannot mmap assemblies directly from a UE PAK without the optional `external_assembly_probe` callback (monodroid-style). The demo sidesteps this by extracting to disk first.

## Goals / Non-Goals

**Goals:**
- Run the same authored C# game scripts on Android arm64 (Development) that run on Win64.
- Bootstrap CoreCLR on Android via the raw `coreclrhost.h` C API, dispatched per-platform from the existing host entry point — no fork of the desktop path.
- Reuse the Mono port's UE staging/packaging pattern (BCL NonUFS / project DLL UFS, `LoadOrder.json`, `PersistentDownloadDir` override) so hot-update of project DLLs works.
- Diagnostics visible via `adb logcat`.
- A reproducible `dotnet publish` + `UAT BuildCookRun` packaging flow.

**Non-Goals:**
- iOS (interpreter/AOT — separate effort, deferred like the Mono port did).
- Android x86 / armeabi-v7a (arm64 only).
- Remote managed debugging on device (documented as future work).
- `external_assembly_probe` / mmap-from-PAK optimization (documented; extraction-to-disk is the initial path).
- Changing the Win64/Mac `hostfxr` behavior.

## Decisions

### 1. Platform dispatch via plain `#if PLATFORM_ANDROID` — no opt-in switch
Add `#if PLATFORM_ANDROID` branches inside `FCSDotNetRuntimeHost` (the Android body lives in a sibling `CSDotNetRuntimeHost_Android.cpp` compiled only on Android) so `InitializeManagedRuntime()` calls the raw-CoreCLR path on Android and the `hostfxr` path elsewhere. The public `FCSDotNetRuntimeHost` interface and the `UnrealSharp.Plugins.Main.InitializeUnrealSharp` entry-point signature stay identical — both paths end at the same managed entry point. **There is no `bUseAndroidCoreClr` ini switch and no Mono-style `UNREALSHARP_ANDROID_CORECLR` macro**: Android is a first-class supported platform, dispatched the same way Win64/Mac are (plain per-platform preprocessor), consistent with the plugin's existing per-platform handling. The runtime engine is CoreCLR on every platform — Android just reaches it through the raw C API instead of hostfxr because hostfxr doesn't exist there.
- **Why**: keeps one host abstraction, one entry point, no per-platform plugin duplication. A switch/macro (the Mono port's `#if UNREALSHARP_MONO` model) is unnecessary here because there is no runtime *choice* on Android — CoreCLR is the only engine, the only difference is the bootstrap API. Plain `#if PLATFORM_ANDROID` mirrors how Win64/Mac are already dispatched.
- **Alt considered**: (a) a separate `UnrealSharpCoreAndroid` module — rejected: doubles maintenance, breaks the single entry-point contract; (b) an opt-in `bUseAndroidCoreClr` switch + `UNREALSHARP_ANDROID_CORECLR` macro (initial design) — rejected on review: Android should "just work" like other platforms, not require configuration.

### 2. Raw `coreclrhost.h` C API, not hostfxr-on-Android
Ship `coreclrhost.h` in `Managed/DotNetRuntime/inc` and call `coreclr_initialize` / `coreclr_create_delegate` / `coreclr_set_error_writer` / `coreclr_shutdown`. Build TPA by scanning the runtime DLL directory (port `build_tpa()` from the demo).
- **Why**: hostfxr is unavailable on Android; the demo proves the raw API works. `coreclr_create_delegate` returns a direct native function pointer to `InitializeUnrealSharp`, equivalent to `load_assembly_and_get_function_pointer` on desktop.
- **Alt considered**: monodroid's `external_assembly_probe` to avoid extraction — deferred (see Decision 4).

### 3. Extract managed DLLs from PAK to a writable runtime dir at first launch
On Android `StartupModule`, ensure `ProjectPersistentDownloadDir()/Managed/Android/` exists and extract BCL + project managed DLLs + `LoadOrder.json` there from the PAK (via `FFileHelper::LoadFileToArray` → write). Use a version/timestamp check (a sentinel file or compare against PAK mtime) to skip re-extraction when unchanged — reusing the Mono port's "touch to update mtime / purge stale" lesson. Point TPA, `APP_CONTEXT_BASE_DIRECTORY`, and `NATIVE_DLL_SEARCH_DIRECTORIES` at this dir.
- **Why**: CoreCLR needs real file paths; `PersistentDownloadDir` is the correct writable, app-private, OS-visible path on Android (Mono port verified `SavedDir` is wrong). PAK stays the source of truth → hot-update drops new DLLs into the PAK / override dir and they extract on next launch.
- **Alt considered**: keep DLLs only in PAK + `external_assembly_probe` — deferred (adds complexity, not yet proven in our stack).

### 4. Native `.so` into `lib/arm64-v8a/` via APL XML; managed DLLs into PAK via UFS `RuntimeDependencies`
Mirror `MonoSDK.Build.cs` + `MonoSDK_APL.xml`: a new `CoreClrSDK.Build.cs` (External module) registers BCL managed DLLs + project DLLs + `LoadOrder.json` as `StagedFileType.UFS` (PAK-resident) — BCL via the `...*.dll` recursive glob against the SDK runtime dir, project DLLs via `*.dll`/`*.pdb`/`*.json` against `Content/Managed/Android/` (only the current platform's subdir, so Win64 DLLs don't leak into the Android APK). CoreCLR native `.so` (`libcoreclr.so`, `libclrjit.so`, `libSystem.*.so`) are staged into the APK `lib/arm64-v8a/` via an `CoreClrSDK_APL.xml` Android Plugin Language file (registered with `AdditionalPropertiesForReceipt.Add("AndroidPlugin", aplPath)`) that `<copyFile>`s them to `$S(BuildDir)/libs/arm64-v8a/` — the proven UE-Android mechanism Gradle uses to land `.so` in the APK. Dedup BCL between the self-contained publish output and the staged copy (Mono port's `DeduplicateBclDlls` pattern).
- **Why**: APL XML is the established UE way to place native `.so` under `lib/<abi>/` so the OS linker loads them at install; managed DLLs want PAK residency for hot-update + smaller install. This split is exactly what the Mono port proved (its `MonoSDK_APL.xml` + `MonoSDK.Build.cs`).
- **Alt considered**: stage native `.so` purely via `RuntimeDependencies(NonUFS)` — rejected: UE-Android `.so` placement under `lib/<abi>/` is APL's job; RuntimeDependencies NonUFS alone is unreliable for Android native libs.

### 5. `dotnet publish -r android-arm64 --self-contained` for BCL/runtime; cross-platform IL for project DLLs
The self-contained publish produces `libcoreclr.so`, `libclrjit.so`, `libSystem.*.so`, and the BCL managed DLLs. Project scripts are cross-platform IL (publish once for `Win64`, copy to `Content/Managed/Android/`), per the Mono port — IL is platform-agnostic. `LoadOrder.json` is copied alongside, exactly as the Mono port did.
- **Why**: self-contained gives the matching runtime+native libs for the target RID; project IL need not be rebuilt per platform.
- **Alt considered**: framework-dependent publish — rejected: no shared runtime installed on Android.

### 6. Diagnostics via `coreclr_set_error_writer` → UE log → logcat
Register an error-writer callback that forwards CoreCLR diagnostics to `UE_LOG(LogUnrealSharp)` (already routed to logcat on Android). Native host logging uses a `CLRHOST`/`CORECLR` tag; managed `Console.WriteLine` lands on the `DOTNET` tag as in the demo.
- **Why**: `DOTNET_HOST_TRACE` is hostfxr-only; the demo confirms `coreclr_set_error_writer` is the Android equivalent.

## Risks / Trade-offs

- **[Extraction cost / first-launch latency]** Copying ~190 BCL DLLs to disk on first launch adds startup time and disk usage. → Mitigation: version-sentinel skip when unchanged; only BCL (runtime-bound) extracts on runtime upgrade. Project DLLs are few.
- **[TPA path correctness]** `coreclr_initialize` fails opaquely if TPA paths are wrong/missing. → Mitigation: port the demo's verified `build_tpa()` scan verbatim; log TPA length + first failure via `coreclr_set_error_writer`; unit-check the dir exists before init.
- **[`coreclr_create_delegate` type-name format]** Demo notes the type arg has no assembly suffix (`"ManagedDemo.ManagedClass"`), unlike `hostfxr`'s `"UnrealSharp.Plugins.Main, UnrealSharp.Plugins"`. → Mitigation: pass `UnrealSharp.Plugins.Main` (no suffix) on Android, `"..., UnrealSharp.Plugins"` on desktop; verify against demo.
- **[Stale extraction cache masking hot-updated DLLs]** If the sentinel logic is wrong, new DLLs in the PAK won't load. → Mitigation: hash/mtime-compare each DLL against PAK source; always re-extract project DLLs whose PAK copy is newer.
- **[APK size]** Self-contained CoreCLR + BCL ≈ tens of MB native + managed. → Trade-off accepted (unavoidable for self-contained); NonUFS native libs are not compressed in PAK.
- **[No `hostfxr` features]** runtimeconfig resolution, hostpolicy, `DOTNET_HOST_TRACE` unavailable. → Trade-off accepted; manual TPA + `coreclr_set_error_writer` cover our needs.
- **[Plugin submodule vs working tree]** The Mono files were removed from the working-tree HEAD; this change re-introduces a mobile host but as CoreCLR, not Mono. Reuse only the *patterns* (packaging, paths, naming), not the Mono code, which is gone.

## Migration Plan

1. Add `coreclrhost.h` + Android host source under `#if PLATFORM_ANDROID` — zero impact on desktop builds (excluded by preprocessor).
2. Add `CoreClrSDK` ThirdParty module (`Source/ThirdParty/CoreClrSDK/`) populated from a `dotnet publish -r android-arm64 --self-contained`: `Android/lib/` (native `.so`) + `Android/runtime/` (BCL `.dll`). `CoreClrSDK.Build.cs` activates unconditionally for Android (no-op on other platforms); `CoreClrSDK_APL.xml` stages the `.so` into `lib/arm64-v8a/`.
3. Add Android publish/packaging command + reuse the project's `BuildManagedCode` flow for cross-platform IL → `Content/Managed/Android/`; document the flow in `BUILD_ANDROID.md`.
4. Add `Android` to `SupportedTargetPlatforms`.
5. **Rollback**: revert the change; desktop behavior is untouched, so rollback is safe and leaves Win64/Mac fully functional.

## Open Questions

- ~~Where do the prebuilt Android CoreCLR + BCL artifacts live?~~ Resolved: `CoreClrSDK` directory populated from a one-off `dotnet publish -r android-arm64 --self-contained` (native `.so` → `Android/lib/`, BCL `.dll` → `Android/runtime/`). A git submodule (mirroring `MonoSDK`) is the eventual home for version control; for now the populated directory suffices.
- ~~Should the extraction use per-DLL mtime comparison or a single runtime-version sentinel?~~ Resolved: per-file mtime comparison (PAK/NonUFS source vs extracted copy); skip extraction when the dest exists and is at least as new as the source. (PAK/NonUFS source mtimes can be bogus, in which case the dest is kept — fast path after first launch.)
- ~~Confirm the `coreclr_create_delegate` entry-point type name format~~ Resolved in code: type name passed **without** assembly suffix (`UnrealSharp.Plugins.Main`), per the demo; `InitializeUnrealSharp` was made `public` because `coreclr_create_delegate` requires a public target (the desktop hostfxr path resolves via delegate and didn't need public). Awaiting device confirmation.
