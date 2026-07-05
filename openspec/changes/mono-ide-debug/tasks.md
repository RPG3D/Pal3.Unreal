# Tasks: Mono IDE Debug Support

## Phase 1: Core Debugger Agent ✅

- [x] 1.1 Add `EMonoDebuggerIDE` enum and debugger settings to `CSUnrealSharpSettings`
- [x] 1.2 Create `FMonoProfilerListener` (fake TCP listener for VSTU compatibility)
- [x] 1.3 Implement `InitMonoDebugger()` in `CSMonoRuntime` with dual-agent support
- [x] 1.4 Implement `EnableVisualStudioToolsForUnitySupport()` (Win64 VSTU artifacts)
- [x] 1.5 Guard AOT mode section with `!GMonoDebuggerActive` in `InitializeMonoRuntime`
- [x] 1.6 Wire `InitMonoDebugger()` + wait-for-debugger into `InitializeMonoHost`
- [x] 1.7 Add `Sockets`, `Networking` module dependencies to Build.cs
- [x] 1.8 Verify INTERP_ONLY vs JIT mode — confirmed JIT required for `GetFunctionPointer()`
- [x] 1.9 Code review: fix TEMP comment, LogTemp→LogUnrealSharp, Rider suspend=n doc, string lifetime
- [x] 1.10 Update `README_MONO.md` with IDE Debugging documentation

## Phase 2: Crash Fix — Variable Expansion ❌

- [ ] 2.1 Investigate root cause: why expanding C# stack variables triggers native calls
- [ ] 2.2 Determine if fix is on C# side (UnmanagedCallbacks) or native side (debugger params)
- [ ] 2.3 Implement and verify fix
- [ ] 2.4 Test with both VS 2022 and Rider

## Phase 3: Polish

- [ ] 3.1 Test with `MonoDebuggerIDE=2` (VS+Rider concurrent)
- [ ] 3.2 Verify packaged Development build (non-Shipping, debugger enabled)
- [ ] 3.3 Document known limitations (variable expansion crash, etc.)
- [ ] 3.4 Clean up and commit
