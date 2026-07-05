# Design: Mono IDE Debug Support

## Architecture

```
┌──────────────────────────────────────────────┐
│     Visual Studio 2026 / JetBrains Rider     │
│     Debug → Attach → 127.0.0.1:56000         │
└──────────────────┬───────────────────────────┘
                   │ Mono Debugger Wire Protocol (TCP)
┌──────────────────▼───────────────────────────┐
│          Unreal Editor Process               │
│  ┌──────────────────────────────────────┐    │
│  │  InitMonoDebugger(Settings)          │    │
│  │  ├─ Port = Settings.MonoDebuggerPort │    │
│  │  ├─ mono_jit_parse_options(         │    │
│  │  │   "--debugger-agent=...:56000")   │    │
│  │  └─ mono_debug_init(MONO_FORMAT)     │    │
│  └──────────────────────────────────────┘    │
│  ┌──────────────────────────────────────┐    │
│  │  InitializeMonoRuntime()             │    │
│  │  ├─ AOT guard: !GMonoDebuggerActive  │    │
│  │  └─ mono_jit_init_version("v4...")   │    │
│  └──────────────────────────────────────┘    │
└──────────────────────────────────────────────┘
```

## Call Sequence

```
CSDotNetRuntimeHost_Mono::InitializeMonoHost()
  ├─ Read UCSUnrealSharpSettings (bMonoPerformanceMode, MonoDebuggerPort, ...)
  ├─ if !bMonoPerformanceMode:
  │   InitMonoDebugger(Settings)          ← BEFORE mono_jit_init
  │     ├─ Build --debugger-agent string
  │     ├─ mono_jit_parse_options(1, Options)
  │     └─ mono_debug_init(MONO_DEBUG_FORMAT_MONO)
  ├─ InitializeMonoRuntime()
  │   ├─ AOT mode: skipped if GMonoDebuggerActive
  │   └─ mono_jit_init_version("UnrealSharp", "v4.0.30319")
  └─ if IsMonoDebuggerActive() && bMonoWaitDebugger:
       Sleep(DelayMonoStartTimeWhenWaitDebugger)
```

## Key Decisions

### 1. JIT mode with soft breakpoints

Mono's debugger agent uses **soft breakpoints** — it patches JIT-compiled native code
with trap instructions at breakpoint locations. The JIT generates debug metadata (stack
frame layouts, local variable locations) alongside the compiled code. This means:

- `MethodHandle.GetFunctionPointer()` returns valid native pointers (JIT has compiled the method)
- The debugger can set breakpoints, step through code, and inspect variables
- No runtime overhead when the debugger is not attached (JIT compiles normally)

**Why not INTERP_ONLY?**

`mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP_ONLY)` disables JIT entirely. While the debugger
works well with the interpreter, `GetFunctionPointer()` fails for all methods — there is no
compiled native code. Our C# glue layer (`UnmanagedCallbacks.GetManagedMethod`) uses
`GetFunctionPointer()` to resolve Blueprint method handles, causing `Failed to update method
handle` fatal errors during Blueprint compilation.

### 2. Single port, single agent

VS 2026's **Attach to Process → Managed (.NET Core)** and Rider's **Mono Remote Debugger**
both support connecting to any TCP port. A single debugger agent on `MonoDebuggerPort`
(default 56000) serves both IDEs. Only one IDE can attach at a time (Mono debugger protocol
limitation — single session per agent).

### 3. Settings via UDeveloperSettings

All debug configuration through `UCSUnrealSharpSettings` with `ConfigRestartRequired` on
`MonoDebuggerPort`. Accessible from Project Settings UI and `DefaultUnrealSharp.ini`.

## Modified Files

| File | Delta |
|------|-------|
| `CSUnrealSharpSettings.h` | Added `MonoDebuggerPort` and debugger UPROPERTYs |
| `CSMonoRuntime.h` | `InitMonoDebugger()` and `IsMonoDebuggerActive()` declarations |
| `CSMonoRuntime.cpp` | `InitMonoDebugger()` — single agent, JIT mode, port validation |
| `CSDotNetRuntimeHost_Mono.cpp` | Call `InitMonoDebugger()` + wait-for-debugger guard |
| `README_MONO.md` | IDE Debugging documentation |
