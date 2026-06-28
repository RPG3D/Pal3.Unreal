export const meta = {
  name: 'mono-reintegration-review',
  description: 'Comprehensive review of the Mono re-integration onto 5.8 upstream (005e8fff)',
  phases: [
    { title: 'Audit', detail: 'parallel per-file stale-revert + coherence audit' },
    { title: 'Verify', detail: 'adversarially verify high-risk findings' },
    { title: 'Synthesize', detail: 'produce the md review report' },
  ],
}

const REPO = 'E:/UEProject/SharpDemo/Plugins/UnrealSharp'
const UPSTREAM = '005e8fff'   // pure upstream tip (5.8)
const MONO_TIP = '1a315484'   // canonical mono tip (base c226e494, pre-5.8)
const MERGE_BASE = '9dee04c8' // merge base of upstream & mono (pre-5.8)

// Files modified by the merge (staged M), excluding submodule.
// These are the files where stale-revert risk exists (5.8 refactor ∩ mono change).
const FILES = [
  '.gitignore',
  'Build/Scripts/BuildCommands/BuildEmitLoadOrder.cs',
  'Build/Scripts/BuildCommands/BuildSolution.cs',
  'Build/Scripts/BuildCommands/PackageProject.cs',
  'Managed/Directory.Build.props',
  'Managed/UnrealSharp/UnrealSharp.Core/GCHandleUtilities.cs',
  'Managed/UnrealSharp/UnrealSharp.Core/ManagedCallbacks.cs',
  'Managed/UnrealSharp/UnrealSharp.Core/UnmanagedCallbacks.cs',
  'Managed/UnrealSharp/UnrealSharp.GlueGenerator/UnrealSharp.GlueGenerator/NativeTypes/UnrealFunctionBase.cs',
  'Managed/UnrealSharp/UnrealSharp.GlueGenerator/UnrealSharp.GlueGenerator/StringBuilderExtensions.cs',
  'Managed/UnrealSharp/UnrealSharp.Plugins/Main.cs',
  'Managed/UnrealSharp/UnrealSharp.Plugins/Plugin.cs',
  'Managed/UnrealSharp/UnrealSharp.Plugins/PluginLoadContext.cs',
  'Managed/UnrealSharp/UnrealSharp.Plugins/PluginLoader.cs',
  'Managed/UnrealSharp/UnrealSharp.Plugins/UnrealSharp.Plugins.csproj',
  'Managed/UnrealSharp/UnrealSharp.StaticVars/BaseStaticVar.cs',
  'Managed/UnrealSharp/UnrealSharp.StaticVars/UnrealSharp.StaticVars.csproj',
  'Source/UnrealSharpCore/Private/Compilers/CSManagedClassCompiler.cpp',
  'Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost.cpp',
  'Source/UnrealSharpCore/Private/Extensions/Libraries/CSActorExtensions.cpp',
  'Source/UnrealSharpCore/Private/Extensions/Libraries/CSObjectExtensions.cpp',
  'Source/UnrealSharpCore/Private/Extensions/Libraries/CSPlayerControllerExtensions.cpp',
  'Source/UnrealSharpCore/Private/Extensions/Libraries/CSWorldExtensions.cpp',
  'Source/UnrealSharpCore/Private/Extensions/Subsystems/CSLocalPlayerSubsystem.cpp',
  'Source/UnrealSharpCore/Private/UnrealSharpCore.cpp',
  'Source/UnrealSharpCore/Public/DotNet/CSDotNetRuntimeHost.h',
  'Source/UnrealSharpCore/Public/Extensions/Libraries/CSMovementComponentExtensions.h',
  'Source/UnrealSharpCore/Public/Extensions/Libraries/CSTextExtensions.h',
  'Source/UnrealSharpCore/Public/Extensions/Subsystems/CSEngineSubsystem.h',
  'Source/UnrealSharpCore/Public/Extensions/Subsystems/CSGameInstanceSubsystem.h',
  'Source/UnrealSharpCore/Public/Extensions/Subsystems/CSLocalPlayerSubsystem.h',
  'Source/UnrealSharpCore/Public/Extensions/Subsystems/SubsystemCollectionBaseRef.h',
  'Source/UnrealSharpCore/Public/Types/CSBlueprint.h',
  'Source/UnrealSharpCore/Public/Types/CSScriptStruct.h',
  'Source/UnrealSharpCore/Public/UnrealSharpCore.h',
  'Source/UnrealSharpCore/UnrealSharpCore.Build.cs',
  'Source/UnrealSharpEditor/Private/HotReload/CSHotReloadSubsystem.cpp',
  'Source/UnrealSharpEditor/Private/UnrealSharpEditor.cpp',
  'Source/UnrealSharpManagedGlue/GlueGenerator.cs',
  'Source/UnrealSharpManagedGlue/SourceGeneration/GeneratorStringBuilderUtilities.cs',
  'Source/UnrealSharpManagedGlue/Utilities/BuildUtilities.cs',
  'Source/UnrealSharpUtilities/Private/CSDotnetUtilties.cpp',
  'Source/UnrealSharpUtilities/Private/CSPathsUtilities.cpp',
  'Source/UnrealSharpUtilities/Private/CSProjectUtilities.cpp',
  'Source/UnrealSharpUtilities/UnrealSharpUtilities.Build.cs',
  'UnrealSharp.uplugin',
]

const FINDING_SCHEMA = {
  type: 'object',
  properties: {
    file: { type: 'string' },
    verdict: { type: 'string', enum: ['clean', 'minor', 'risk', 'bug'] },
    summary: { type: 'string' },
    staleReverts: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          what: { type: 'string' },        // e.g. "TRACE_CPUPROFILER_EVENT_SCOPE removed"
          upstreamCommit: { type: 'string' },
          severity: { type: 'string', enum: ['cosmetic', 'functional', 'build-break'] },
        },
        required: ['what', 'severity'],
      },
    },
    monoAdditionsIntact: { type: 'boolean' },
    notes: { type: 'string' },
  },
  required: ['file', 'verdict', 'summary', 'staleReverts', 'monoAdditionsIntact'],
}

phase('Audit')

const auditResults = await pipeline(
  FILES,
  // Stage 1: audit one file
  (f) => agent(
    `You are reviewing a git MERGE result for a Mono runtime re-integration onto UnrealSharp (an Unreal Engine C# plugin).

Repo: ${REPO} (cd there; use git)
File under review: ${f}

Context:
- UPSTREAM = ${UPSTREAM} (pure 5.8 upstream tip — the "ours" side of the merge)
- MONO_TIP = ${MONO_TIP} (canonical Mono integration, base c226e494 which is PRE-5.8 — the "theirs" side)
- MERGE_BASE = ${MERGE_BASE} (pre-5.8 common ancestor)
- The merge was done with: \`git merge --no-commit 1a315484\` while HEAD=${UPSTREAM}.
- The merge is UNCOMMITTED; the reviewed file content is the WORKING TREE version.

THE CRITICAL REVIEW QUESTION for each file:
  Is the working-tree file equal to (UPSTREAM + Mono-only additions), with NO stale reverts of 5.8 upstream improvements?

Why this matters: Mono's tree (MONO_TIP) is based on pre-5.8 code. If the merge took "theirs" for shared (non-Mono-guarded) code, it would silently REVERT 5.8 refactors. We must ensure all 5.8 upstream improvements in this file are PRESERVED, and only Mono's \`#if UNREALSHARP_MONO\` guards / Mono-specific logic are ADDED on top.

Steps:
1. Run: \`cd ${REPO} && git diff ${UPSTREAM} -- ${f}\` — this shows the working-tree (merged) file vs pure upstream. This is the EFFECTIVE delta the merge introduces.
2. Classify every changed hunk:
   - "mono-addition": a \`#if UNREALSHARP_MONO\` guard, Mono-specific include/branch, or genuinely Mono-only logic. GOOD.
   - "stale-revert": a change that REMOVES or ALTERS 5.8 upstream code in shared (non-guarded) regions — e.g. removing TRACE_CPUPROFILER_EVENT_SCOPE, reverting a method rename, reverting a refactor (AddModuleInterfaceInit/FindModule, dictionary→list, etc.), reverting dialog text, reverting a 5.8 include reorganization. BAD.
   - "conflict-resolution": a manual merge decision (e.g. keeping both an upstream method and a Mono factory). Note it.
3. For any "stale-revert", identify the upstream commit if possible (\`git log --oneline ${MERGE_BASE}..${UPSTREAM} -- ${f}\`) and severity.
4. Also check Mono additions are INTACT (Mono guards present and well-formed; no orphaned #else/#endif; signatures consistent).

Be precise and adversarial. A "clean" verdict requires that \`git diff ${UPSTREAM}\` shows ONLY mono-additions (+ conflict-resolutions that keep both sides), never deletions of 5.8 shared code.

Return the finding object. If the file is trivially clean (e.g. only adds one #if UNREALSHARP_MONO include), say so quickly.`,
    { label: `audit:${f.split('/').pop()}`, phase: 'Audit', schema: FINDING_SCHEMA }
  ),
  // Stage 2: adversarially verify any non-clean finding
  (finding) => {
    if (!finding) return null
    if (finding.verdict === 'clean') return finding
    return agent(
      `Adversarially verify this Mono re-integration review finding. Try to REFUTE it (prove it's actually clean) before confirming.

Repo: ${REPO}
Finding for file ${finding.file}: verdict=${finding.verdict}
Summary: ${finding.summary}
Alleged stale reverts: ${JSON.stringify(finding.staleReverts)}

Verification method:
1. \`cd ${REPO} && git diff ${UPSTREAM} -- ${finding.file}\` — re-examine the actual delta vs pure 5.8 upstream.
2. For each alleged stale-revert, check whether the "removed" 5.8 code is genuinely gone from the working tree, or whether it's actually preserved (finding was a false alarm).
3. Conversely, if the finding says "clean" but you spot a deletion of 5.8 shared code (a TRACE macro, a method, a refactor) that is NOT inside a Mono #if guard and NOT a deliberate conflict-resolution, that's a real bug the first reviewer missed.
4. Check Mono guard balance: count \`#if UNREALSHARP_MONO\` vs \`#endif\` in the file — must be balanced (each #if has matching #endif, accounting for #else).

Return a verdict object: { file, confirmed: boolean, actualVerdict: 'clean'|'minor'|'risk'|'bug', correctedSummary, evidence }`,
      { label: `verify:${finding.file.split('/').pop()}`, phase: 'Verify', schema: {
        type: 'object',
        properties: {
          file: { type: 'string' },
          confirmed: { type: 'boolean' },
          actualVerdict: { type: 'string', enum: ['clean', 'minor', 'risk', 'bug'] },
          correctedSummary: { type: 'string' },
          evidence: { type: 'string' },
        },
        required: ['file', 'confirmed', 'actualVerdict', 'correctedSummary'],
      } }
    ).then(v => ({ original: finding, verify: v }))
  }
)

const verified = auditResults.filter(Boolean)

phase('Synthesize')

// Cross-cutting coherence checks (C#/C++ symbol matching, build system, runtime init)
const coherenceChecks = await parallel([
  () => agent(
    `Check C#/C++ managed-callback symbol coherence after the Mono re-integration merge.
Repo: ${REPO} (cd there; use git/grep)

The 5.8 upstream RENAMED the managed-callback methods (e.g. LookupManagedMethod → GetManagedMethod, LookupManagedType → GetManagedTypeHandle) in BOTH C++ (CSManagedCallbacksCache.h, CSManagedAssembly.cpp) and C# (ManagedCallbacks.cs, UnmanagedCallbacks.cs). Mono's tree used the OLD names (LookupManagedMethod).

After merge, verify the C++ and C# sides AGREE on names and signatures:
1. C++ side: \`grep -rn "GetManagedMethod\|LookupManagedMethod\|GetManagedTypeHandle\|LookupManagedType" Source/UnrealSharpCore/Public/CSManagedCallbacksCache.h Source/UnrealSharpCore/Private/CSManagedAssembly.cpp\`
2. C# side: \`grep -rn "GetManagedMethod\|LookupManagedMethod\|GetManagedTypeHandle\|LookupManagedType" Managed/UnrealSharp/UnrealSharp.Core/ManagedCallbacks.cs Managed/UnrealSharp/UnrealSharp.Core/UnmanagedCallbacks.cs\`
3. The C# UnmanagedCallbacks methods must have signatures matching the C++ typedefs (CSManagedCallbacksCache.h: e.g. \`ManagedCallbacks_GetManagedMethod = uint8*(__stdcall*)(void*, const TCHAR*)\` → C# \`IntPtr GetManagedMethod(IntPtr, char*)\`). Mono's version used \`nint\` params — verify the MERGED C# uses \`char*\` (matching 5.8 C++) not \`nint\`.
4. Check the C# method NAMES referenced in ManagedCallbacks.cs (the struct field initializers like \`GetManagedMethod = &UnmanagedCallbacks.GetManagedMethod\`) actually exist as [UnmanagedCallersOnly] methods in UnmanagedCallbacks.cs.

Report: { coherent: boolean, mismatches: string[], details: string }`,
    { label: 'coherence:callbacks', phase: 'Synthesize', schema: { type:'object', properties:{ coherent:{type:'boolean'}, mismatches:{type:'array',items:{type:'string'}}, details:{type:'string'}}, required:['coherent','mismatches','details'] } }
  ),
  () => agent(
    `Check build-system coherence of the Mono re-integration.
Repo: ${REPO} (cd there; use git/grep)

Verify:
1. UnrealSharpCore.Build.cs and UnrealSharpUtilities.Build.cs both add "MonoSDK" to their dependency module lists. \`grep -n MonoSDK Source/UnrealSharpCore/UnrealSharpCore.Build.cs Source/UnrealSharpUtilities/UnrealSharpUtilities.Build.cs\`
2. MonoSDK.Build.cs (Source/ThirdParty/MonoSDK/MonoSDK.Build.cs) defines UNREALSHARP_MONO=1 for Win64/Mac/Android/IOS when bUseMono=true, and UNREALSHARP_MONO=0 otherwise. Confirm the Win64 branch exists and links coreclr. \`grep -n "UNREALSHARP_MONO\|Win64\|coreclr" Source/ThirdParty/MonoSDK/MonoSDK.Build.cs\`
3. UnrealSharpCore.Build.cs reads bUseMono from DefaultEngine.ini and (a) skips the CoreCLR DotNetRuntime/inc include path when bUseMono, (b) adds USE_MONO_RUNTIME definition. Confirm.
4. Managed/Directory.Build.props defines UNREALSHARP_MONO when UseMonoRuntime=true. \`grep -n UNREALSHARP_MONO Managed/Directory.Build.props\`
5. The .csproj files (UnrealSharp.Plugins.csproj, UnrealSharp.StaticVars.csproj) use \`$(DefineConstants);WITH_EDITOR\` (append, not overwrite) so UNREALSHARP_MONO from Directory.Build.props is preserved. \`grep -n DefineConstants Managed/UnrealSharp/UnrealSharp.Plugins/UnrealSharp.Plugins.csproj Managed/UnrealSharp/UnrealSharp.StaticVars/UnrealSharp.StaticVars.csproj\`
6. BuildUtilities.cs (UnrealSharpManagedGlue) reads bUseMono from ini and injects -p:UseMonoRuntime=true for UHT. Confirm present.
7. PackageProjectMono.cs + MonoProjectSettings.cs exist (new Mono packaging command). Confirm.

Report: { coherent: boolean, issues: string[], details: string }`,
    { label: 'coherence:build', phase: 'Synthesize', schema: { type:'object', properties:{ coherent:{type:'boolean'}, issues:{type:'array',items:{type:'string'}}, details:{type:'string'}}, required:['coherent','issues','details'] } }
  ),
  () => agent(
    `Check runtime-init coherence of the Mono re-integration (the editor-launch failure path).
Repo: ${REPO} (cd there; use git/grep)

Context: On first editor launch with bUseMono=true, the editor showed "UnrealSharp can't be initialized. An installation of .NET 10.0.0 SDK can't be found" in a loop, then OOM'd (exit 130). Three fixes were applied AFTER the merge:
  (a) GetDotNetDirectory() in Source/UnrealSharpUtilities/Private/CSDotnetUtilties.cpp — was matching PATH marker "Program Files\\dotnet\\" WITH trailing backslash; Windows PATH entry "C:\\Program Files\\dotnet" (no trailing) didn't match → returned empty. Fixed to normalize trailing separators + EndsWith + return path WITH trailing separator (callers do GetDotNetDirectory()+"dotnet.exe").
  (b) StartupModule() in Source/UnrealSharpCore/Private/UnrealSharpCore.cpp — had unbounded self-recursion on VerifyCSharpEnvironment failure → OOM. Fixed with static retry counter, max 5.
  (c) VerifyCSharpEnvironment() — dialog text was not logged. Added UE_LOG.

Verify these three fixes are correctly implemented in the WORKING TREE (read the actual current files):
1. Read Source/UnrealSharpUtilities/Private/CSDotnetUtilties.cpp GetDotNetDirectory: confirm (i) trailing-separator stripping, (ii) EndsWith match with marker WITHOUT trailing slash, (iii) returned path has trailing separator so \`GetDotNetDirectory()+"dotnet.exe"\` is valid. Check GetDotNetExecutablePath still works.
2. Read Source/UnrealSharpCore/Private/UnrealSharpCore.cpp StartupModule: confirm the static retry counter + MaxStartupRetries=5 + the recursion is gated by \`++count <= Max\`. Confirm it cannot recurse infinitely.
3. Read VerifyCSharpEnvironment: confirm UE_LOG added on both error paths.
4. Sanity: under Mono (UNREALSHARP_MONO=1), trace the startup path — does VerifyCSharpEnvironment still run? (Canonical mono commit 5 removed the !UNREALSHARP_MONO guard, so YES it runs under Mono; with the path fix dotnet is found so it passes.) Is this acceptable, or should VerifyCSharpEnvironment be skipped under Mono? Give your assessment.
5. Check the recursion: is \`static int32 StartupRetryCount\` function-local static safe across the retries? (Yes — it persists across calls in the same process.) Any concern that a successful path never resets it? (StartupModule is called once at module load, so this is fine.)

Report: { fixesCorrect: boolean, issues: string[], runtimeRiskAssessment: string }`,
    { label: 'coherence:runtime', phase: 'Synthesize', schema: { type:'object', properties:{ fixesCorrect:{type:'boolean'}, issues:{type:'array',items:{type:'string'}}, runtimeRiskAssessment:{type:'string'}}, required:['fixesCorrect','issues','runtimeRiskAssessment'] } }
  ),
])

const [cbCoherence, buildCoherence, runtimeCoherence] = coherenceChecks.filter(Boolean)

// Final synthesis agent: produce the markdown report content (returned as text)
const report = await agent(
  `You are the lead reviewer synthesizing a comprehensive review report for a Mono runtime re-integration onto UnrealSharp (Unreal Engine C# plugin).

Produce a Markdown report. Be concrete, cite file paths, and give an executive verdict. The report will be saved as a .md file for the developer.

## Context (write this into the report's background section)
- Goal: re-integrate Mono runtime onto the \`mono202606\` branch of submodule \`Plugins/UnrealSharp\`, which had been reset to upstream \`origin/main\` to merge 5.8-fixes. The backup patch (E:\\Code\\UnrealSharp-mono202606.patch, base c226e494 pre-5.8) no longer applied.
- Method chosen: \`git merge --no-commit 1a315484\` (canonical mono tip) while HEAD=005e8fff (5.8 upstream). Merge base 9dee04c8 (pre-5.8).
- WHY merge (not cherry-pick/patch): cherry-picking the prior attempt 7efe8d15 silently reverted 5.8 refactors (it's the 1a315484 tree replayed onto a2d2cd47). The standalone patch was incomplete (missed core C++ integration). Merge preserves 5.8 single-sided refactors while applying mono.
- 13 conflicts resolved; 7.8 Export/ dir deleted by 5.8 (modify/delete); 6 content conflicts (ManagedCallbacks, UnmanagedCallbacks, Plugin.cs, PluginLoader.cs, CSScriptStruct.h, CSHotReloadSubsystem.cpp).
- One 5.8 API adaptation: CSDotNetRuntimeHost_Mono.cpp CSBindsManager→CSBindsRegistry.
- Compile-verified: editor C++ (Mono + CoreCLR), managed bindings + user scripts. 0 errors.
- First runtime launch FAILED (" .NET 10.0.0 SDK can't be found" loop → OOM). Three post-merge fixes applied: dotnet PATH trailing-separator, StartupModule recursion cap (5), VerifyCSharpEnvironment logging.
- State: merge UNCOMMITTED, 53 files staged, MonoSDK @ 4a6bc8a7, bUseMono=true. Editor NOT re-launched after fixes (per user instruction, avoid OOM).

## Per-file audit results (from the parallel review)
${JSON.stringify(verified.map(v => ({ original: v.original, verify: v.verify })), null, 2)}

## Cross-cutting coherence checks
- C#/C++ callback coherence: ${JSON.stringify(cbCoherence)}
- Build-system coherence: ${JSON.stringify(buildCoherence)}
- Runtime-init coherence (the 3 fixes): ${JSON.stringify(runtimeCoherence)}

## Required report sections
1. **Executive Summary** — one-paragraph verdict + a status table (compile / runtime / risk).
2. **Method & Why** — merge vs cherry-pick/patch rationale (concise).
3. **Conflict Resolution** — table of the 13 conflicts + resolution strategy.
4. **5.8 API Adaptations** — the CSBindsRegistry rename, and any others found.
5. **Stale-Revert Audit** — aggregate findings from per-file review. List ANY file where 5.8 shared code was reverted (even cosmetically). If none, say so explicitly with confidence.
6. **Coherence Checks** — C#/C++ symbols, build system, runtime init.
7. **Runtime Failure & Fixes** — the editor-launch OOM root cause + the 3 fixes, with correctness assessment.
8. **Risks & Open Items** — e.g. pre-existing Win64 BCL DllImport issue, the question of whether VerifyCSharpEnvironment should be skipped under Mono, runtime/PIE/Android not yet verified.
9. **Recommendations** — next steps (re-launch after fixes, verify PIE, package Win64/Android).
10. **Verdict** — is this re-integration sound for the developer to proceed with runtime verification?

Write the full markdown now. Use tables where helpful. Do NOT wrap in code fences — output raw markdown.`,
  { label: 'synthesize:report', phase: 'Synthesize' }
)

return { report, perFileCount: verified.length, coherence: { callbacks: cbCoherence, build: buildCoherence, runtime: runtimeCoherence } }
