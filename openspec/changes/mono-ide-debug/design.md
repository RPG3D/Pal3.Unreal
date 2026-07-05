# Design: Mono IDE Debug Support

## Architecture

```
┌──────────────────────────────────────────────┐
│         Visual Studio 2022 / Rider           │
│     Debug → Attach → <port>                  │
└──────────────┬───────────────┬───────────────┘
               │ VS port       │ Rider port
               │ 56000+pid%1k  │ 57000
┌──────────────▼───────────────▼───────────────┐
│          Unreal Editor Process               │
│  ┌──────────────────────────────────────┐    │
│  │  InitMonoDebugger(Settings)          │    │
│  │  ├─ EnableVisualStudioToolsForUnity… │    │
│  │  │   → {Exe}_Data/, UnityPlayer.dll  │    │
│  │  ├─ mono_jit_parse_options(         │    │
│  │  │   "--debugger-agent=...:56384",   │    │
│  │  │   "--debugger-agent=...:57000")   │    │
│  │  ├─ mono_debug_init(MONO_FORMAT)     │    │
│  │  └─ FMonoProfilerListener(55987)     │    │
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
  ├─ Read UCSUnrealSharpSettings (bMonoPerformanceMode, MonoDebuggerIDE, ...)
  ├─ if !bMonoPerformanceMode:
  │   InitMonoDebugger(Settings)          ← BEFORE mono_jit_init
  │     ├─ #if PLATFORM_WINDOWS: EnableVisualStudioToolsForUnitySupport()
  │     ├─ Build --debugger-agent strings per IDE
  │     ├─ mono_jit_parse_options(N, Options)
  │     ├─ mono_debug_init(MONO_DEBUG_FORMAT_MONO)
  │     └─ new FMonoProfilerListener(FakedMonoProfilerPort)
  ├─ InitializeMonoRuntime()
  │   ├─ AOT mode: skipped if GMonoDebuggerActive
  │   └─ mono_jit_init_version("UnrealSharp", "v4.0.30319")
  └─ if bMonoWaitDebugger: Sleep(DelayMonoStartTimeWhenWaitDebugger)
```

## Key Decisions

### 1. JIT mode over INTERP_ONLY

`mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP_ONLY)` 会禁用 JIT，导致 `MethodHandle.GetFunctionPointer()` 返回无效指针。这影响了 `UnmanagedCallbacks.GetManagedMethod()` — C# 胶水层用反射获取 Blueprint method handle 的核心路径。

**测试验证**: INTERP_ONLY 模式下出现 3 次 `Failed to update method handle for ReceiveBeginPlay in ScriptCharacter_C`；JIT 模式下 0 次错误。

### 2. Dual-agent over single-agent

Mono debugger-agent 支持多个实例同时监听不同端口。同时启动 VS 和 Rider agent 让用户无需重启 Editor 即可切换 IDE。

### 3. EMonoDebuggerIDE enum over boolean flags

用枚举 `EMonoDebuggerIDE { VS2022, Rider, VS2022_Rider }` 代替之前的 `bEnableRiderDebuggerSupport` boolean。Project Settings UI 中更直观。

### 4. Settings via UDeveloperSettings

所有配置通过 `UCSUnrealSharpSettings` 的 `UPROPERTY(ConfigRestartRequired=true)` 暴露在 Project Settings UI，同时支持 `DefaultUnrealSharp.ini` 文件配置。

## New Files

| File | Purpose |
|------|---------|
| `FMonoProfilerListener.h/cpp` | 假 TCP 监听器，欺骗 VS Tools for Unity |

## Modified Files

| File | Delta |
|------|-------|
| `CSUnrealSharpSettings.h` | `EMonoDebuggerIDE` 枚举 + 9 个 debugger 配置属性 |
| `CSUnrealSharpSettings.cpp` | Shipping 自动 `bMonoPerformanceMode = true` |
| `CSMonoRuntime.h` | `InitMonoDebugger()` 声明 |
| `CSMonoRuntime.cpp` | `InitMonoDebugger()`, `EnableVisualStudioToolsForUnitySupport()`, AOT 守卫, includes |
| `CSDotNetRuntimeHost_Mono.cpp` | 调用 `InitMonoDebugger()` + wait-for-debugger 逻辑 |
| `UnrealSharpCore.Build.cs` | 添加 `Sockets`, `Networking` 依赖 |
| `README_MONO.md` | IDE Debugging 文档 |

## Current Issue

展开 C# 堆栈变量时触发 native call 崩溃。这个问题的根因是 UnrealSharp 的 C#-native 交互方式：查看变量时调试器调用 property getter，而 getter 可能触发 native 代码（如 `GCHandleUtilities.GetObjectFromHandlePtr`），导致 Mono debugger-agent 线程在 GC unsafe 状态下执行 native 代码。需要研究是否可以通过 `mono_threads_enter_gc_unsafe_region` 保护或调整 debugger-agent 参数来解决。
