## Why

The `mono202606` branch of the `Plugins/UnrealSharp` submodule previously carried a full Mono runtime integration (5 commits, `a6d3380`..`1a315484`) enabling C# scripting on Android/iOS via `libmonosgen-2.0` alongside the default CoreCLR host. That branch was reset to upstream `origin/main` to merge the latest upstream (5.8-fixes, hot-reload crash fixes), dropping the Mono work. The original backup patch (`E:\Code\UnrealSharp-mono202606.patch`) no longer applies cleanly because its base (`c226e494`) is ~30+ upstream commits behind the current tip (`005e8fff`).

This change re-integrates the Mono runtime onto the current `mono202606` tip (`005e8fff`) so the dual-runtime architecture (CoreCLR default + Mono opt-in via `bUseMono=true`) works again on the latest upstream code.

## What Changes

- **Re-apply the canonical Mono integration** onto `Plugins/UnrealSharp` @ `005e8fff`. Source: the prior re-integration commit `7efe8d15` ("feat(mono): integrate Mono runtime into latest upstream"), which is byte-for-byte identical to the canonical mono tip `1a315484` on all Mono-specific and key conflict files, but based on a recent upstream (`a2d2cd47`).
- **Reconcile 2 post-`a2d2cd47` upstream hot-reload commits** (`29275c49`, `005e8fff`) that touch `CSHotReloadSubsystem.cpp` — the only substantive conflict area. Resolution keeps both Mono's `#if UNREALSHARP_MONO` guards and upstream's `RefreshStructs` relocation / `OnStructRebuilt` body change.
- **Re-register the MonoSDK submodule** (`Source/ThirdParty/MonoSDK`) at commit `4a6bc8a7` (already present in the working tree) and restore `.gitmodules`. (Canonical `1a315484` pointed at `7446733a`; either is acceptable — see design.)
- **No new files / no new logic** beyond what the canonical integration already established. This is a mechanical re-application, not a redesign.

## Capabilities

### Modified Capabilities
- `mono-runtime-integration`: Restored on the latest upstream. CoreCLR remains default (`bUseMono=false`); Mono is opt-in (`bUseMono=true` in `DefaultEngine.ini`). All `#if UNREALSHARP_MONO` dual-path branches, the UFS assembly preload hook, the Mono packaging command, and the BCL NonUFS / project-DLL UFS staging strategy are re-established. Hot-reload under Mono keeps using `dotnet build` (Roslyn `UnrealSharp.Editor.dll` is skipped) and now coexists with upstream's hot-reload crash fixes.

## Impact

- **Code** (submodule `Plugins/UnrealSharp`): ~61 files touched — 7 new Mono files (`CSMonoRuntime.h/.cpp`, `CSDotNetRuntimeHost_Mono.cpp`, `PluginLoadContext_Mono.cs`, `PackageProjectMono.cs`, `MonoProjectSettings.cs`, `README_MONO.md`), plus `#if UNREALSHARP_MONO` guards / `#include` additions across Core, Editor, Utilities, GlueGenerator, and managed C# projects.
- **Submodule**: `Plugins/UnrealSharp` checked out on `mono202606` branch; the re-integration is committed (or staged) on that branch. Outer `SharpDemo` repo records the updated submodule pointer.
- **Config**: `Config/DefaultEngine.ini` already has (or gets) `[UnrealSharp] bUseMono=true` to exercise the Mono path.
- **Build**: `UnrealSharpCore` / `UnrealSharpUtilities` gain a `MonoSDK` module dependency and `UNREALSHARP_MONO` define driven by `bUseMono`. Editor + packaged Win64 + packaged Android (arm64) must build and run.
- **Risk**: Low–medium. The conflict surface is essentially one file (`CSHotReloadSubsystem.cpp`); the rest applies cleanly because `7efe8d15` already absorbed the `c226e494..a2d2cd47` upstream delta. Main residual risks: (a) MonoSDK binary presence in the working tree, (b) Win64 packaged `DllImport` of BCL native libs (pre-existing open issue from prior integration — see `mono-integration-status` memory), (c) the 5.8-specific engine fixes referenced in memory may need re-confirmation on this upstream.
- **Constraint**: Per user instruction, **do not commit / do not push**. Leave the re-integration in the working tree (staged or unstaged) for review.
