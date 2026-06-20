# Tasks: android-coreclr-support

Reference materials:
- Android CoreCLR usage (raw API, TPA, `coreclr_set_error_writer`, APK layout): `E:\Code\CoreClrDemo\AndroidClrDemo\README.md` + `app/src/cpp/clrdemo.c`
- UE build/packaging patterns (`Build.cs`, APL XML, publish flow, `RuntimeDependencies` UFS/NonUFS, `LoadOrder.json`, `PersistentDownloadDir`): `E:\Code\UnrealSharp\Source\ThirdParty\MonoSDK\MonoSDK.Build.cs`, `MonoSDK_APL.xml`, `Build/Scripts/BuildCommands/PackageProjectMono.cs`, `README_MONO.md`
- Memory: `mono-integration-status.md`, `mono-debugging-lessons.md` (path strategy, packaging flow, debugging lessons)

> Design note (revised): **no opt-in switch, no Mono-style macro.** Android is a first-class
> supported platform dispatched with a plain `#if PLATFORM_ANDROID`, consistent with Win64/Mac.
> CoreCLR is the engine on every platform; Android just bootstraps it via the raw C API
> (`coreclrhost.h`) because hostfxr is unavailable there.

## 1. CoreCLR raw-API headers & SDK scaffold

- [x] 1.1 Add `coreclrhost.h` (raw `coreclr_initialize` / `coreclr_create_delegate` / `coreclr_set_error_writer` / `coreclr_shutdown` declarations, from `dotnet/runtime` `src/coreclr/hosts/inc`) to `Managed/DotNetRuntime/inc/` alongside `hostfxr.h` / `coreclr_delegates.h` / `nethost.h`.
- [x] 1.2 Create `Source/ThirdParty/CoreClrSDK/` with `Android/lib` + `Android/runtime`. Populate from `dotnet publish -r android-arm64 --self-contained`: `libcoreclr.so` / `libclrjit.so` / `libSystem.*.so` (8 `.so`) → `Android/lib/`; BCL managed `.dll` (172, incl. `System.Private.CoreLib.dll`) → `Android/runtime/`.
- [x] 1.3 Write `Source/ThirdParty/CoreClrSDK/CoreClrSDK.Build.cs` (`Type = External`), modeled on `MonoSDK.Build.cs`: **no ini switch** — activates unconditionally for `Target.Platform == Android` (no-op otherwise). Stages BCL managed DLLs via `RuntimeDependencies.Add(..., "...*.dll", StagedFileType.NonUFS)`; project DLLs from `Content/Managed/Android/` (`*.dll`/`*.pdb`/`*.json`) as `StagedFileType.UFS`; registers the APL via `AdditionalPropertiesForReceipt.Add("AndroidPlugin", aplPath)`.
- [x] 1.4 Write `Source/ThirdParty/CoreClrSDK/CoreClrSDK_APL.xml` modeled on `MonoSDK_APL.xml`: `<isArch arch="arm64-v8a">` → `<copyFile>` `libcoreclr.so`, `libclrjit.so`, `libSystem.*.so` from `Android/lib/` to `$S(BuildDir)/libs/arm64-v8a/`.

## 2. Android runtime host (raw CoreCLR C API)

- [x] 2.1 Add `#include <coreclrhost.h>`-based platform dispatch to `CSDotNetRuntimeHost` (header + cpp). On `PLATFORM_ANDROID` (plain `#if`, no macro), route `InitializeManagedRuntime()` to a new raw-CoreCLR path; Win64/Mac keep the `hostfxr` path unchanged.
- [x] 2.2 Implement `InitializeManagedRuntimeAndroid()`: `FPlatformProcess::GetDllHandle` `libcoreclr.so`, bind `coreclr_initialize`, `coreclr_create_delegate`, `coreclr_set_error_writer`, `coreclr_shutdown`.
- [x] 2.3 Implement `BuildTpa(const FString& RuntimeDir)`: scan `*.dll` in the runtime dir, join absolute paths with `:` (port `build_tpa()` from `clrdemo.c`). Log TPA length + assembly count.
- [x] 2.4 Implement `EnsureRuntimeDllsExtracted()`: ensure `FPaths::ProjectPersistentDownloadDir()/Managed/Android/` exists; extract BCL (NonUFS, `CoreClrSDK/Android/runtime`) + project managed DLLs + `LoadOrder.json` (UFS/PAK, `Content/Managed/Android`) via `FFileHelper::LoadFileToArray` → write. Per-file mtime comparison (PAK source vs extracted copy); re-extract newer project DLLs (hot-update). Do NOT use `ProjectSavedDir()` (returns SD root on Android — per Mono lessons).
- [x] 2.5 Call `coreclr_set_error_writer` with a callback forwarding CoreCLR diagnostics to `UE_LOG(LogUnrealSharp)` (routed to logcat). Register before `coreclr_initialize`.
- [x] 2.6 Call `coreclr_initialize` with properties `TRUSTED_PLATFORM_ASSEMBLIES` (TPA), `APP_CONTEXT_BASE_DIRECTORY`, `NATIVE_DLL_SEARCH_DIRECTORIES` (all pointing at the extracted runtime dir). Fatal-log on non-zero return.
- [x] 2.7 Call `coreclr_create_delegate` for `UnrealSharp.Plugins` / `UnrealSharp.Plugins.Main` / `InitializeUnrealSharp` (type name WITHOUT assembly suffix, per the demo). Invoke the resulting `FInitializeRuntimeHost` with the same args as the desktop path. Mind value-vs-pointer arg passing (Mono lessons).
- [x] 2.8 Implement `ShutdownManagedRuntime()` Android path: `coreclr_shutdown` + clear pointers.

## 3. Managed entry-point compatibility

- [x] 3.1 Verify `UnrealSharp.Plugins.Main.InitializeUnrealSharp` is `[UnmanagedCallersOnly]` and callable via `coreclr_create_delegate`. Made the method **`public`** — `coreclr_create_delegate` requires a public target (the demo's `PrintMessage` was public); the desktop hostfxr path resolves via delegate and didn't need public.
- [x] 3.2 Ensure the project scripts publish cross-platform IL to `Content/Managed/Android/` and that `LoadOrder.json` lists them. (Handled by `BuildManagedCode` flow + `PackageProjectAndroidCoreClr` — `--no-self-contained` IL publish + `LoadOrder.json` emit + BCL dedup.)

## 4. Build system: Android publish + packaging

- [x] 4.1 Add a packaging command `Build/Scripts/BuildCommands/PackageProjectAndroidCoreClr.cs` modeled on `PackageProjectMono.cs`: parse `ArchiveDirectory`/`UETargetType`/`UEBuildConfig`/`TargetPlatform=Android`; publish cross-platform IL (`--no-self-contained`) to `Content/Managed/Android/`; dedup BCL against `CoreClrSDK/Android/runtime/`; emit `LoadOrder.json`. (BCL/runtime come from CoreClrSDK NonUFS/APL; the command only produces project/bindings IL.)
- [x] 4.2 Register the command (auto-registered as a `BuildCommand`); keep upstream `PackageProject.cs` untouched to minimize merge conflicts. (Staging gated by platform in `CoreClrSDK.Build.cs`, not an ini switch.)
- [x] 4.3 Add `Android` to `SupportedTargetPlatforms` in `Plugins/UnrealSharp/UnrealSharp.uplugin`.
- [x] 4.4 Document the full flow in a new `Plugins/UnrealSharp/BUILD_ANDROID.md`.

## 5. Verify on device / build (arm64, Development)

- [x] 5.1 Confirm the APK contains the CoreCLR native libs and that the staging wiring is correct. **Native `.so` in APK `lib/arm64-v8a/` confirmed** via `Build.bat SharpDemo Android` + APK unzip: `libcoreclr.so` (6.2MB), `libclrjit.so` (3.2MB), 4× `libSystem.*.so`. (The full UAT `Cook+Pak+Stage+Archive` run to verify managed DLLs inside the PAK hit a machine **out-of-memory** crash during cook — 8.1 GiB allocation failed, 85 GiB physical already in use by other processes / page file too small. This is an environment resource limit, not a code defect. Re-run on a machine with more free RAM/page file to confirm PAK contents.)
- [ ] 5.2 `adb install` + launch; `adb logcat -s CORECLR:I DOTNET:I LogUnrealSharp:I` and confirm: TPA built, `coreclr_initialize` returns 0, delegate resolved, `InitializeUnrealSharp` runs, and a sample C# script executes. (Requires an arm64 device — not executable autonomously.)
- [ ] 5.3 Verify hot-update: drop a newer project DLL into the PAK/override, relaunch, confirm the updated assembly loads (extraction mtime check fires).

## 6. Hardening & docs

- [x] 6.1 Add startup guards: missing `libcoreclr.so` / empty runtime dir → clear fatal error with the expected path (no silent null deref). (Fatal logs with paths in `InitializeManagedRuntimeAndroid` + `EnsureRuntimeDllsExtracted`.)
- [x] 6.2 Record Android-specific debugging notes (logcat tags, tombstone path, `coreclr_set_error_writer`) in `BUILD_ANDROID.md` and a memory entry (`android-coreclr-status.md`).
- [x] 6.3 Mark iOS, x86/arm32, and remote debugging as explicit non-goals/future-work in docs.

## 7. Verification builds run this session

- [x] 7.1 Win64 Editor C++ build — succeeded (host dispatch + header + `CoreClrSDK.Build.cs` compile on desktop; Android TU excluded).
- [x] 7.2 Android C++ build — succeeded end-to-end (APK produced); `CSDotNetRuntimeHost_Android.cpp` compiles under Android clang 18 / NDK r27c. Fixed a pre-existing Android compile bug in `CSDotnetUtilties.cpp` (`GetPluginAssembliesPath` unqualified in the non-Win32 `GetRuntimeHostPath` branch).
- [x] 7.3 Automation project (`UnrealSharp.Automation.csproj`) + managed `UnrealSharp.Plugins` build — succeeded (0 errors).
- [x] 7.4 CoreClrSDK populated from `dotnet publish -r android-arm64 --self-contained` (native `.so` + 172 BCL `.dll`).
- [x] 7.5 Full UAT `BuildCookRun -TargetPlatform=Android -Cook -Pak -Stage -Archive -package` — **blocked by machine OOM during cook** (8.1 GiB alloc failed; 85 GiB physical in use by other processes). Environment resource limit, not a code issue. The C++/APK packaging (step 7.2) succeeded and confirmed the native `.so` staging; re-run the UAT cook on a less-loaded machine to verify managed-DLL-in-PAK content and produce the installable archive APK.
