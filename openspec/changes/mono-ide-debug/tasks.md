# Tasks: Mono IDE Debug Support

## Phase 1: Core Debugger Agent ✅

- [x] 1.1 Add debugger settings (`MonoDebuggerPort`, `bMonoWaitDebugger`, etc.) to `CSUnrealSharpSettings`
- [x] 1.2 Implement `InitMonoDebugger()` — single agent, JIT mode, port validation
- [x] 1.3 Add `IsMonoDebuggerActive()` accessor for wait-for-debugger guard
- [x] 1.4 Guard AOT mode section with `!GMonoDebuggerActive` in `InitializeMonoRuntime`
- [x] 1.5 Wire `InitMonoDebugger()` + wait-for-debugger into `InitializeMonoHost`
- [x] 1.6 Code review & fixes: zombie comments, duplicate headers, port validation, embedding docs
- [x] 1.7 Verify JIT mode required for `GetFunctionPointer()` (INTERP_ONLY breaks BP method handles)
- [x] 1.8 Verify VS 2026 and Rider both connect to single `MonoDebuggerPort=56000`
- [x] 1.9 Update `README_MONO.md` with simplified IDE Debugging docs + JIT/soft-breakpoint rationale
- [x] 1.10 Update OpenSpec artifacts (design.md, spec.md, proposal.md)

## Phase 2: Polish

- [x] 2.1 Simplify from dual-agent to single-agent architecture (VS/Rider share port)
- [x] 2.2 Remove VSTU compatibility hacks (EnableVisualStudioToolsForUnitySupport, FMonoProfilerListener)
- [x] 2.3 Remove `Sockets`/`Networking` deps from Build.cs
- [x] 2.4 Remove `EMonoDebuggerIDE` enum — single port serves both IDEs
- [ ] 2.5 Test with packaged Development build (non-Shipping, debugger enabled)
- [ ] 2.6 Merge to `mono202606` and upstream `main`

## Notes

- Variable expansion crash resolved by using JIT mode instead of INTERP_ONLY.
- Mono's soft breakpoints work with JIT-compiled code — the debugger patches native code at breakpoint locations.
- `GetFunctionPointer()` requires JIT (no native pointer in INTERP mode).
