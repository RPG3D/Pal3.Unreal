export const meta = {
  name: 'mono-soundness-review',
  description: 'Pre-commit technical-soundness review of the Mono re-integration architecture',
  phases: [
    { title: 'Analyze', detail: 'parallel deep-dive on each architectural dimension' },
    { title: 'Synthesize', detail: 'produce the soundness review md report' },
  ],
}

const REPO = 'E:/UEProject/SharpDemo/Plugins/UnrealSharp'

// Each agent deep-dives ONE architectural dimension of the Mono integration.
// Focus: is the technical approach SOUND (not just "does it compile")?
// Repo facts: merge of 1a315484 (mono, base c226e494 pre-5.8) onto 005e8fff (5.8 upstream).
// Verified working: Win64 + Android packaged both run, C# GameMode loads.

const DIMENSIONS = [
  {
    key: 'dual-runtime-guards',
    prompt: `Review the SOUNDNESS of the dual-runtime (CoreCLR/Mono) guard strategy in the Mono integration.
Repo: ${REPO} (cd there; use grep/git).

The integration uses '#if UNREALSHARP_MONO' / '#if !UNREALSHARP_MONO' guards to coexist CoreCLR (default) and Mono (opt-in via bUseMono). UNREALSHARP_MONO is defined by MonoSDK.Build.cs when bUseMono=true.

Assess:
1. List all files using these guards (grep -rn "UNREALSHARP_MONO" Source/ Managed/). Classify each guard as: (a) genuinely-needed behavioral split, (b) could-be-abstracted-behind-interface, (c) gratuitous/could-be-removed.
2. Is defining UNREALSHARP_MONO via a ThirdParty module (MonoSDK.Build.cs) sound? Trace: does it propagate to ALL modules that #if on it? (UnrealSharpCore + UnrealSharpUtilities depend on MonoSDK; does UnrealSharpEditor? Check Build.cs deps.) Any module that uses the guard but doesn't depend on MonoSDK would get UNREALSHARP_MONO=0 always (or undefined) — a latent bug.
3. The build-time choice (bUseMono at build, not runtime) — is this the right granularity? Any place where a runtime check would be safer?
4. Guard balance/sanity: any #if without #endif, any #else branch that's dead under both configs?

Verdict: is the guard strategy sound? What are the real risks (not theoretical)? Give a confidence level.`,
  },
  {
    key: 'assembly-loading-path',
    prompt: `Review the SOUNDNESS of the Mono assembly loading + path strategy.
Repo: ${REPO}.

The integration loads assemblies via mono_install_assembly_preload_hook → LoadAssemblyFromUFS → FFileHelper::LoadFileToArray (UE I/O, PAK-aware). Read Source/UnrealSharpCore/Private/DotNet/CSMonoRuntime.cpp and Source/UnrealSharpUtilities/Private/CSPathsUtilities.cpp.

Assess:
1. The GetUserAssemblyDirectory() change: Mono packaged branch now returns FPaths::ConvertRelativePathToFull(ProjectContentDir/Managed/Platform). Is ConvertRelativePathToFull the RIGHT API here? Does it produce a path that IFileManager resolves against PAK mounts correctly? (Verified working on Win64+Android, but explain WHY it works — is it robust or lucky?)
2. Search paths (GMonoAssemblySearchPaths): SavedOverrideDir (absolute) + AssembliesPath parts. The override dir is ProjectSavedDir/Managed/Platform — is using Saved/ as a writable override location sound for Android (where Saved/ maps to PersistentDownloadDir)? Check the override path matches what memory notes say (ProjectPersistentDownloadDir, not ProjectSavedDir, for Android).
3. PurgeStaleOverrideDlls: compares exe mtime vs DLL mtime, deletes DLLs older than exe. Is this sound? What if the exe is older (legitimate downgrade)? What about Android where exe path is GAPKFilename?
4. BCL (NonUFS, absolute) vs project DLLs (UFS, in PAK) split — is the layering principled? Any DLL that's ambiguously placed?
5. The "MONO_PATH doesn't exist" warnings (relative paths in MONO_PATH env, PAK-internal dirs not on disk) — harmless or a smell?

Verdict: is the path/loading strategy sound and robust across platforms?`,
  },
  {
    key: 'callback-interop',
    prompt: `Review the SOUNDNESS of the C#↔C++ managed-callback interop under Mono.
Repo: ${REPO}.

Read Source/UnrealSharpCore/Public/CSManagedCallbacksCache.h, Source/UnrealSharpCore/Private/CSManagedAssembly.cpp, Managed/UnrealSharp/UnrealSharp.Core/ManagedCallbacks.cs, Managed/UnrealSharp/UnrealSharp.Core/UnmanagedCallbacks.cs.

The 5.8 upstream renamed callbacks (LookupManagedMethod→GetManagedMethod) and Mono uses a DIFFERENT invocation strategy: under Mono, LookupManagedMethod stores a MethodInfo GCHandle (not a function pointer) and InvokeManagedMethod uses reflection Invoke; under CoreCLR it uses delegate* function pointers.

Assess:
1. Is the dual invocation strategy (reflection-invoke under Mono, func-ptr under CoreCLR) sound? Performance implications (reflection Invoke vs direct call)? The README claims ~1.6x Mono JIT vs CoreCLR — is that consistent with reflection-invoke, or is there a cheaper Mono path missed?
2. The signature choice: C# GetManagedMethod(IntPtr, char*) vs Mono's original nint variant. The merge kept char* (matching 5.8 C++). Is char* (UTF-16 TCHAR) correct for Mono on all platforms? Any marshalling concern?
3. The "copy native string IMMEDIATELY before any other operations" comment in UnmanagedCallbacks — is this a real Mono INTERP bug workaround, or cargo-cult? Does the fix actually address a heap-relocation race, or is it masking a deeper issue?
4. InvokeManagedMethod's reflection path: methodInfo.Invoke(managedObject, new object[]{argumentsBuffer, returnValueBuffer}) — allocates an object[] every call. Sound for a hot interop path? Any boxing/GC pressure concern?
5. mono_runtime_invoke for InitializeUnrealSharp (5 args, pointer passing rules) — is the args[] passing (pointers-by-value vs &var for value types) correct per Mono's rules? Any latent bug?

Verdict: is the interop approach sound, or is it a "works but fragile" situation?`,
  },
  {
    key: 'build-system-ini-coupling',
    prompt: `Review the SOUNDNESS of build-system coupling to DefaultEngine.ini.
Repo: ${REPO}.

The integration reads bUseMono from Config/DefaultEngine.ini [UnrealSharp] in MULTIPLE places: MonoSDK.Build.cs, UnrealSharpCore.Build.cs (also adds USE_MONO_RUNTIME def), BuildUtilities.cs (UHT, reads ini directly), MonoProjectSettings.cs, BuildManagedCode.bat.

Assess:
1. Is reading the SAME ini key from 5 different code paths (C# Build.cs, C# automation, bat) sound, or is it duplication that can drift? Any path that could disagree (e.g. UHT reads ini differently than UBT)?
2. The UnrealSharpCore.Build.cs adds "USE_MONO_RUNTIME=" + (bUseMono?1:0) as a PublicDefinition — but the comment says it's "for UHT plugin to inject -p:UseMonoRuntime". Is this actually used anywhere, or dead? grep for USE_MONO_RUNTIME.
3. BuildManagedCode.bat now parses ini with findstr to decide UseMonoRuntime. Is a bat parsing ini (vs calling the C# automation that already does it correctly) sound, or reinventing? Could the bat just delegate to a BuildSolution command?
4. The .csproj DefineConstants: Directory.Build.props adds UNREALSHARP_MONO when UseMonoRuntime=true, but Plugins.csproj/StaticVars.csproj do $(DefineConstants);WITH_EDITOR (append). Is the ordering/append correct? Could UNREALSHARP_MONO be lost if a csproj overwrites DefineConstants?
5. bUseMono is a single global switch — but Android CoreCLR (AndroidClr branch) doesn't use it (platform-automatic). Is the switch design forward-compatible with AndroidClr coexistence, or will it conflict?

Verdict: is the ini-coupling sound, or fragile/duplicated?`,
  },
  {
    key: 'merge-vs-rebase-decision',
    prompt: `Review the SOUNDNESS of the merge-based re-integration decision and its long-term implications.
Repo: ${REPO}.

Context: the re-integration used 'git merge 1a315484' (mono tip, base c226e494 pre-5.8) onto 005e8fff (5.8 upstream). Merge base 9dee04c8 (pre-5.8). The merge is UNCOMMITTED (53 files staged). Prior attempts (cherry-pick 7efe8d15, standalone patch) were rejected because they silently reverted 5.8 refactors.

Assess:
1. Is merging a feature branch whose base is FAR behind (c226e494 → 9dee04c8, many commits) sound long-term? The merge commit will have a huge diff and obscure history. Future merges of upstream will re-conflict on the same 6 files. Is there a better long-term structure (e.g. rebase mono onto 5.8 once, then maintain)?
2. The merge is uncommitted — committing creates a merge commit with 2 parents (005e8fff + 1a315484). Is that the right history shape, or should this be a linear rebase (replay mono commits onto 005e8fff)?
3. The 5.8 API adaptations made during merge (CSBindsManager→CSBindsRegistry, TSharedString, FString(Pair.Key)) — are these captured as commit-able changes, or will they be lost if the merge is aborted? (They're in the working tree/index.)
4. The mono branch (1a315484) is based on pre-5.8 — its OWN history contains the old API names. If someone later cherry-picks an old mono commit, they'll reintroduce the stale names. Is the merge capturing the adaptation, or should the mono branch itself be rebased to 5.8?
5. Risk: the merge preserved 5.8 refactors (good) but the mono branch's FUTURE commits still assume pre-5.8 APIs. Next time mono work continues, does it branch from the merged (5.8) state or the old 1a315484?

Verdict: is the merge approach sound for a one-time re-integration, and what's the recommended going-forward branching strategy?`,
  },
  {
    key: 'platform-specific-paths',
    prompt: `Review the SOUNDNESS of platform-specific path handling (Win64/Android/iOS/Mac) in the Mono integration.
Repo: ${REPO}.

Read CSMonoRuntime.cpp (platform AOT modes, ManagedPlatformDir), CSDotNetRuntimeHost_Mono.cpp (PlatformDir), CSPathsUtilities.cpp (GetUserAssemblyDirectory platform branches), MonoSDK.Build.cs (per-platform linking).

Assess:
1. The "PLATFORM_WINDOWS→Win64, PLATFORM_MAC→Mac, PLATFORM_ANDROID→Android, PLATFORM_IOS→IOS/IOSSimulator" mapping is duplicated in 3+ places (CSMonoRuntime, CSPathsUtilities, CSDotNetRuntimeHost_Mono). Is this duplication sound, or a drift risk? Any place where the mapping disagrees?
2. FPlatformProperties::PlatformName() returns "Windows" (not "Win64") — the code works around this with PLATFORM_ macros. Is this workaround principled, or should there be a single helper? Any place that forgot the workaround (uses PlatformName() directly and would break)?
3. iOS INTERP+AOT mode (mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP), mono_aot_register_module) vs Android JIT (MONO_AOT_MODE_NONE) — is the platform dispatch sound? The mono_interp.flag file for forcing INTERP on non-iOS — is that a debug-only feature accidentally left in production paths?
4. PurgeStaleOverrideDlls uses FPlatformProcess::ExecutablePath() on Win64/Mac but GAPKFilename on Android — is the Android path correct? Any platform where the override dir check would misbehave?
5. MonoSDK.Build.cs stages BCL as NonUFS via "...*.dll" glob — is the "..." (recursive) glob sound, or could it stage unintended DLLs?

Verdict: is the platform path handling sound and consistent?`,
  },
]

phase('Analyze')

const analyses = await parallel(DIMENSIONS.map(d => () =>
  agent(d.prompt, {
    label: `analyze:${d.key}`,
    phase: 'Analyze',
    schema: {
      type: 'object',
      properties: {
        dimension: { type: 'string' },
        verdict: { type: 'string', enum: ['sound', 'mostly-sound', 'needs-attention', 'unsound'] },
        confidence: { type: 'string', enum: ['high', 'medium', 'low'] },
        strengths: { type: 'array', items: { type: 'string' } },
        risks: { type: 'array', items: { type: 'object', properties: {
          issue: { type: 'string' },
          severity: { type: 'string', enum: ['cosmetic', 'low', 'medium', 'high'] },
          evidence: { type: 'string' },
          recommendation: { type: 'string' },
        }, required: ['issue', 'severity', 'recommendation'] } },
        details: { type: 'string' },
      },
      required: ['dimension', 'verdict', 'confidence', 'strengths', 'risks', 'details'],
    },
  })
))

const valid = analyses.filter(Boolean)

phase('Synthesize')

const report = await agent(
  `You are the lead reviewer synthesizing a PRE-COMMIT technical-soundness review of a Mono runtime re-integration into UnrealSharp (Unreal Engine C# plugin).

This is NOT a "does it compile / did conflicts resolve" review (already done). This is about whether the ARCHITECTURE and TECHNICAL DECISIONS are sound for the long term. The integration is verified WORKING (Win64 + Android packaged both run, C# GameMode loads), so "it works" is not the bar — "is it the right design" is.

## Context
- Mono re-integrated onto 5.8 upstream (005e8fff) via git merge of 1a315484 (mono tip, base c226e494 pre-5.8). Merge base 9dee04c8.
- 53 files staged, UNCOMMITTED (pre-commit review).
- Dual runtime: CoreCLR default + Mono opt-in via bUseMono in DefaultEngine.ini. UNREALSHARP_MONO define.
- Assembly loading: mono_install_assembly_preload_hook → FFileHelper::LoadFileToArray (PAK-aware UFS). BCL NonUFS, project DLLs UFS.
- C#/C++ interop: reflection-invoke under Mono, func-ptr under CoreCLR.
- Verified: Win64 + Android arm64 packaged both run with C# GameMode.
- AndroidClr branch (separate, CoreCLR on Android) coexists; iOS still in research.

## Per-dimension analyses
${JSON.stringify(valid, null, 2)}

## Required report (raw markdown, no code fences)
1. **Executive Verdict** — one paragraph: is this sound to commit? confidence? top 3 risks.
2. **Per-dimension findings** — for each of the 6 dimensions: verdict, key strength, key risk (table + brief).
3. **Cross-cutting themes** — patterns across dimensions (e.g. ini-coupling duplication, path-mapping duplication).
4. **Must-fix before commit** — only HIGH severity issues that block commit. If none, say so.
5. **Should-fix soon (post-commit)** — MEDIUM severity, non-blocking.
6. **Acceptable technical debt** — LOW/cosmetic, document and move on.
7. **Going-forward recommendation** — branching strategy, how to keep mono maintainable against upstream.
8. **Final go/no-go** — commit or not, with conditions.

Be concrete, cite files. Distinguish "verified working" from "sound design". Do not rubber-stamp — if something is "works but fragile", say so.`,
  { label: 'synthesize:soundness-report', phase: 'Synthesize' }
)

return { report, dimensions: valid.length }
