# Pre-Commit Technical Soundness Review: Mono Runtime Re-Integration

**Date:** 2026-06-28
**Reviewer:** Claude (6-dimension parallel analysis, then **human-verified against runtime evidence**)
**Scope:** Technical/architectural soundness of the Mono re-integration onto 5.8 upstream (`005e8fff`), pre-commit.
**State reviewed:** 53 files staged, UNCOMMITTED, `bUseMono=true`, MonoSDK @ `4a6bc8a7`.
**Runtime evidence:** Win64 packaged ✅ + Android arm64 packaged ✅ (both run, C# `ScriptGameMode_C` loads).

> **Important:** Two of the workflow's three "HIGH severity" findings (MF-1 char* marshalling, MF-3 ProjectSavedDir) were **disproven by verification** against engine headers and runtime logs. They are documented in §5 as false-positives so future reviewers don't re-chase them. The real findings are MF-2 (merge history) + the medium/low items.

---

## 1. Executive Verdict

**GO — commit the merge.** The dual-runtime architecture is sound, the integration is verified working on both target platforms (Win64 + Android), and the code-level design (guards, interop layering, path strategy) holds up to scrutiny. The single structural issue worth acting on is the **merge-history shape** (MF-2): the mono branch remains based on pre-5.8 code, which will cause recurring conflicts on future upstream syncs. That's a post-commit branching task, not a code blocker.

Confidence: **high** — every claim below is tied to engine headers, git history, or runtime logs, not the review agents' assertions (two of which were wrong).

| Dimension | Verdict | Confidence |
|-----------|---------|------------|
| Dual-runtime guards | Sound | high |
| Assembly loading + paths | Sound | high |
| C#/C++ interop | Sound | high (MF-1 disproven) |
| Build-system ini coupling | Needs attention | high |
| Merge-based re-integration | Needs attention | high |
| Platform path handling | Mostly sound | high |

---

## 2. Per-Dimension Findings

### Dual-runtime guards — Sound ✅
- All 19 files with `#if UNREALSHARP_MONO` have balanced `#if`/`#endif` (corroborated by successful C++/C# compiles).
- `UNREALSHARP_MONO=0` (explicitly defined, not left undefined) when Mono is off — avoids silent wrong-branch compilation under `-Wundef`.
- `MonoSDK.Build.cs` defines the macro; `UnrealSharpCore` + `UnrealSharpUtilities` depend on `MonoSDK` (verified in Build.cs) so the macro propagates correctly.
- **Risk (low):** `CSDotNetRuntimeHost.h` includes `<mono/jit/jit.h>` in a **public** header. Any transitive includer pulls Mono types. Should be PIMPL/opaque-pointer + include in `.cpp`. Non-blocking.

### Assembly loading + paths — Sound ✅
- BCL (NonUFS, absolute path) vs project DLLs (UFS, in PAK) layering is principled and matches the stated hot-update design.
- `mono_install_assembly_preload_hook` → `LoadAssemblyFromUFS` → `FFileHelper::LoadFileToArray` correctly intercepts all assembly resolution and routes through UE I/O (PAK-aware). Verified: `UnrealSharp.Plugins.dll` loads from PAK on both Win64 + Android.
- `ConvertRelativePathToFull` in `GetUserAssemblyDirectory()` is the **correct** API — PAK mounts key on absolute paths; relative paths fail to match when CWD ≠ exe dir (this was the actual root cause of the earlier packaged-launch crash, now fixed).
- The verbose `Resolving assembly 'X' across N search paths` + per-path `miss` logging added during this review makes the loading chain diagnosable.

### C#/C++ interop — Sound ✅ (MF-1 disproven — see §5)
- Dual invocation (reflection-`Invoke` under Mono, `delegate*` func-ptr under CoreCLR) is architecturally correct. Mono can't safely use `GetFunctionPointer()` (SIGSEGV in `mono_method_get_unmanaged_wrapper_ftnptr_internal`), so reflection-invoke is the right fallback.
- `FGCHandle` + per-ALC `ConcurrentDictionary` for strong references is a robust memory-management design.
- `mono_runtime_invoke` arg passing (pointers by value, value-types via `&var`) follows Mono's documented rules.
- **Risk (medium, perf):** `InvokeManagedMethod` under Mono allocates `new object[]{args, retVal}` per call → GC pressure on hot interop. README's 1.6× Mono-vs-CoreCLR figure is consistent with this. Optimization (SF-1) is post-commit.

### Build-system ini coupling — Needs attention ⚠
- `bUseMono` is read from `DefaultEngine.ini [UnrealSharp]` in **5 places**: `MonoSDK.Build.cs`, `UnrealSharpCore.Build.cs` (both UBT, use `ConfigCache.ReadHierarchy` — hierarchy-aware), `BuildUtilities.cs` (UHT, manual flat parser — **not** hierarchy-aware), `MonoProjectSettings.cs` (automation, manual), `BuildManagedCode.bat` (findstr).
- **Real risk:** UHT (`BuildUtilities.cs`) ignores platform .ini overrides, so on platforms with platform-specific ini layers, UBT and UHT can disagree on `bUseMono`. Low likelihood today (no platform overrides in use) but a latent drift bug.
- `USE_MONO_RUNTIME` C++ define in `UnrealSharpCore.Build.cs:61` is **dead code** — zero references. The correct define (`UNREALSHARP_MONO`) comes from `MonoSDK.Build.cs`. Remove the dead one (ATD-1).
- `Directory.Build.props` append pattern (`$(DefineConstants);UNREALSHARP_MONO`) is correct; all current `.csproj` files use append. Convention is unenforced but not currently broken.

### Merge-based re-integration — Needs attention ⚠ (MF-2 is the real finding)
- The merge correctly preserved all 22 5.8 commits including the major `Bind_` refactor (`CSBindsManager`→`CSBindsRegistry`, deleted `Export/` dir). No lingering conflict markers.
- **MF-2 (the one real HIGH):** Merge base `9dee04c8` is pre-5.8. Git stores it in the merge commit's ancestry, so every future `git merge main` into the mono lineage re-uses it as the 3-way base → all 21 overlapping files re-conflict every time. The mono branch at `1a315484` still contains pre-5.8 API assumptions (modifies files 5.8 deleted).
- **Fix (post-commit):** rebase the mono branch onto the merge commit:
  `git rebase --onto <merge-commit> c226e494 mono202606` — replays the 5 mono commits onto 5.8, eliminating the structural conflict permanently.
- The 5.8 API adaptations made during merge (`CSBindsRegistry`, `TSharedString`, `FString(Pair.Key)`) are captured in the working tree/index and will be in the merge commit — safe.

### Platform path handling — Mostly sound ✅
- `PLATFORM_`→directory-name mapping (`Win64`/`Mac`/`Android`/`IOS`/`IOSSimulator`) is **identical** across all 3 C++ sites (`CSMonoRuntime.cpp`, `CSPathsUtilities.cpp`, `CSDotNetRuntimeHost_Mono.cpp`) and `MonoSDK.Build.cs`. No disagreement.
- `FPlatformProperties::PlatformName()` returns `Windows` (not `Win64`) — correctly avoided via `PLATFORM_` macros in hot paths. One site (`CSDotnetUtilties.cpp:95`) uses `PlatformName()` for Android/Mac SDK paths (works because those names match) — minor inconsistency (ATD-5).
- iOS INTERP+AOT vs Android JIT dispatch is correct. `mono_interp.flag` debug override runs in production with only `Log`-level output (SF-6) — should be gated `WITH_EDITOR` or `Warning`.
- `PurgeStaleOverrideDlls` uses `GAPKFilename` on Android (correct), `ExecutablePath()` elsewhere.

---

## 3. Cross-Cutting Themes

**Theme 1 — Pattern duplication with drift risk.**
- **Ini parsing:** 5 implementations of the same `bUseMono` read. 2 hierarchy-aware (UBT), 3 not (UHT, automation, bat). Can drift on platform overrides.
- **Platform-path mapping:** 3 near-identical `#if PLATFORM_X → "name"` cascades. Adding Linux touches 3 files.
- Both are low-risk today (stable values, no overrides), real maintenance debt when platforms/config change.

**Theme 2 — Merge-history debt.** The merge captures the correct end state but leaves the mono branch pre-5.8. Two consequences: recurring conflicts (MF-2) + wrong-base trap for future branching. Single fix: post-commit rebase.

**Theme 3 — The "verified working" blind spot (refined).** The workflow flagged this as "Android risks can't be caught on Windows." After verification: **both flagged Android risks (MF-1, MF-3) were false** — TCHAR is UTF-16 on both Win64 and Android (MF-1 wrong), and `ProjectSavedDir()` on Android is the correct app-private writable path per the AndroidClr reference implementation (MF-3 wrong). The real lesson: review agents reading stale memory notes can produce confident-but-wrong platform claims; always cross-check against engine headers + the parallel AndroidClr implementation.

---

## 4. Must-Fix Before Commit (HIGH)

**None.** The two workflow-flagged HIGH items (MF-1, MF-3) were disproven (§5). MF-2 (merge history) is a post-commit branching action, not a code blocker — the merge commit itself is clean and correct.

---

## 5. False-Positives Disproven by Verification

### MF-1 (workflow): "char* marshalling breaks Mono on non-Windows" — FALSE
- **Workflow claim:** non-Windows `TCHAR` is UTF-8, C# `char*` is UTF-16 → mismatch on Android.
- **Engine header evidence:** `WindowsPlatform.h:45` `typedef UTF8CHAR TCHAR` is gated by `#if USE_UTF8_TCHARS`; `USE_UTF8_TCHARS` is **not defined anywhere** in the engine → defaults 0 → Win64 `TCHAR = WIDECHAR = UTF-16`. `AndroidPlatform.h:25` `typedef WIDECHAR TCHAR` → Android `TCHAR = UTF-16`. **Both platforms UTF-16.**
- **Runtime evidence:** Android packaged build runs, C# `ScriptGameMode_C` loads (requires `GetManagedMethod` UFUNCTION lookups to succeed). If `char*` were mis-marshalled, no method would resolve and no C# GameMode would load. Win64 packaged also runs.
- **Conclusion:** C# `char*` (UTF-16) ↔ C++ `const TCHAR*` (UTF-16) matches on both verified platforms. MF-1 is wrong. (Caveat: if a future engine build flips `USE_UTF8_TCHARS=1`, Win64 would break — but that's hypothetical, not current.)

### MF-3 (workflow): "ProjectSavedDir on Android returns SD card root" — FALSE
- **Workflow claim:** `ProjectSavedDir()` on Android returns unscoped SD card path; should use `ProjectPersistentDownloadDir()`.
- **Reference implementation:** the AndroidClr branch (same author, separate branch) **deliberately uses `ProjectSavedDir()`** with a detailed comment: `ProjectPersistentDownloadDir` is scanned by the PAK file system and interferes with PAK reads of managed DLLs; `ProjectSavedDir` on Android is the app-private writable path.
- **Runtime evidence:** Android logcat shows `../../../SharpDemo/Saved/Managed/Android/` containing real override DLLs from prior test runs, correctly read and purged by `PurgeStaleOverrideDlls` — the path is writable, readable, and functional.
- **Memory note was stale:** the `mono-integration-status` memory saying "ProjectSavedDir returns SD root on Android" reflects an older engine version; 5.8 + the AndroidClr design confirm `ProjectSavedDir` is correct.
- **Conclusion:** MF-3 is wrong. `ProjectSavedDir()` is the right choice and matches the AndroidClr reference.

---

## 6. Should-Fix Soon (MEDIUM, post-commit)

| # | Issue | File(s) | Recommendation |
|---|-------|---------|----------------|
| SF-1 | Per-call `new object[]{}` allocation in Mono reflection-invoke → GC pressure on hot interop | `UnmanagedCallbacks.cs:255` | Use `MethodInfo.CreateDelegate()` for Mono JIT mode; `Invoke` only for INTERP. Perf win, not correctness. |
| SF-2 | UHT (`BuildUtilities.cs`) manual ini parser ignores platform overrides; can disagree with UBT | `BuildUtilities.cs` | Use `ConfigCache.ReadHierarchy` in UHT context, or centralize both manual parsers. |
| SF-3 | `BuildManagedCode.bat` findstr ini parse = 5th independent implementation | `BuildManagedCode.bat` | Acceptable for now (works, section key is unique); consider delegating to C# automation later. |
| SF-4 | Mono branch at `1a315484` modifies files 5.8 deleted; cherry-picking old mono commits onto 5.8 fails | mono branch history | Same fix as MF-2 (post-commit rebase). |
| SF-5 | `bUseMono` is single global bool; can't express "Android=CoreCLR, Mac=Mono" for AndroidClr coexistence | `MonoSDK.Build.cs` | Refactor to per-platform keys when AndroidClr+Mono coexistence is needed. Not urgent. |
| SF-6 | `mono_interp.flag` debug override runs in production, `Log`-level only | `CSMonoRuntime.cpp:362` | Gate `#if WITH_EDITOR` or log `Warning`. |
| SF-7 | `<mono/jit/jit.h>` in public header pollutes transitive includers | `CSDotNetRuntimeHost.h:10` | PIMPL / opaque pointer; move include to `.cpp`. |

---

## 7. Acceptable Technical Debt (LOW/cosmetic)

- **ATD-1:** `USE_MONO_RUNTIME` C++ define (`UnrealSharpCore.Build.cs:61`) is dead — zero references. Remove.
- **ATD-2:** `BuildUtilities.cs` and `MonoProjectSettings.cs` have near-identical manual ini parsers. Consolidate.
- **ATD-3:** `Directory.Build.props` append convention for `UNREALSHARP_MONO` is unenforced. Add a lint check or rely on review.
- **ATD-4:** "Copy native string IMMEDIATELY" comment in `UnmanagedCallbacks.cs` justifies a real defensive copy with a slightly inaccurate rationale (Mono allocator vs FString invalidation). Update comment.
- **ATD-5:** `CSDotnetUtilties.cpp:95` uses `PlatformName()` for Android/Mac SDK paths while line 106 hardcodes `Win64` — inconsistent with the explicit-macro pattern elsewhere. Use literals.
- **ATD-6:** `CSMonoRuntime.cpp:330` `PLATFORM_LINUX` branch for `MONO_THREADS_SUSPEND` is dead (Mono never enabled on Linux). Remove or comment as forward-looking.
- **ATD-7:** 3-way duplication of `PLATFORM_`→dir-name cascade. Extract `GetMonoPlatformDir()` helper.
- **ATD-8:** UFS paths in `mono_set_assemblies_path` cause spurious "MONO_PATH doesn't exist" warnings. Pass only on-disk BCL dirs to `mono_set_assemblies_path`; keep UFS paths in the preload hook only.
- **ATD-9:** `PurgeStaleOverrideDlls` is destructive on legitimate APK downgrade. Move to `.stale` suffix or version-tag.
- **ATD-10:** `GetManagedMethod` linear `DeclaredMethods` search — fine, result is cached at bind time, not per-invocation.

---

## 8. Going-Forward Recommendation

1. **Commit the merge now** with a message enumerating: the merge source (`1a315484`), the 5.8 API adaptations (`CSBindsRegistry`, `TSharedString`/`FString(Pair.Key)`, `CSGlobalSettingsUtilities`), the runtime fixes (dotnet PATH detection, recursion cap, `ConvertRelativePathToFull`, LoadOrder copy, `UseMonoRuntime` injection), and the `BuildManagedCode.bat` rewrite.
2. **Immediately rebase the mono branch** onto the merge commit (`git rebase --onto <merge> c226e494 mono202606`) to fix MF-2. Force-push or create `mono202606-5.8`.
3. **Future mono work** branches from the rebased mono branch, never from pre-rebase `1a315484`.
4. **AndroidClr coexistence:** track SF-5 (per-platform `bUseMono`) as a known limitation for when AndroidClr+Mono must coexist.
5. **Optional cleanup pass** for ATD-1/4/5/6 (trivial, low-risk).

---

## 9. Final Go/No-Go

**GO — commit.** No code blockers. The single HIGH item (MF-2) is a post-commit branching action. The two workflow HIGH items (MF-1, MF-3) were disproven by engine-header + runtime-log verification. The integration is architecturally sound and verified working on both target platforms. Address SF-1/SF-2/SF-6/SF-7 and the ATD items incrementally post-commit.

**Methodology note:** the 6-dimension parallel review was valuable for surfacing the duplication themes, the dead `USE_MONO_RUNTIME` define, the public-header Mono include, and the merge-history debt. But two of its three HIGH findings were confidently-wrong platform claims derived from stale memory notes rather than engine headers. The verification step (checking `WindowsPlatform.h`/`AndroidPlatform.h` TCHAR typedefs + the AndroidClr reference `ProjectSavedDir` choice + runtime logs) was essential — a naive "trust the review" commit would have introduced unnecessary `byte*`/`PersistentDownloadDir` changes that *regress* a working integration.
