# Spec: Mono Debugger

## Overview

Mono Soft Debugger Agent 集成，支持 IDE 断点调试 C# 脚本。

## Requirements

### R1: Breakpoint Debugging
- Editor 启动时自动启动 debugger agent
- VS 2022 通过 Attach Unity Debugger 可设置断点、单步执行
- Rider 通过 Mono Remote Debugger 可设置断点、单步执行

### R2: IDE Selection
- 支持三种模式：仅 VS、仅 Rider、VS+Rider 同时
- 通过 `EMonoDebuggerIDE` 枚举配置
- Mono 运行时初始化后更改需重启 Editor

### R3: VS Tools for Unity Compatibility
- 自动创建 `{ExeName}_Data/` 和 `UnityPlayer.dll`
- Debugger 端口遵循 VSTU 公式 `56000 + pid % 1000`
- 假 Profiler TCP 监听器 (55987) 满足新版 VSTU 要求

### R4: Shipping Exclusion
- Shipping 构建自动禁用所有 debugger 代码
- 可通过 `bMonoPerformanceMode` 手动禁用

### R5: JIT Runtime Mode
- 使用 JIT 模式而非 INTERP_ONLY
- 确保 `MethodHandle.GetFunctionPointer()` 能正常解析 Blueprint method handles

## Platform Support

| Platform | Status |
|----------|--------|
| Win64 Editor | ✅ In progress (variable expansion crash) |
| Win64 Packaged | Not tested |
| Mac Editor | Not implemented |
| Android | Not implemented |
| iOS | Not supported (no JIT) |

## Configuration

```ini
[/Script/UnrealSharpCore.CSUnrealSharpSettings]
MonoDebuggerIDE=0           # 0=VS, 1=Rider, 2=VS+Rider
RiderDebuggerDefaultPort=57000
bMonoWaitDebugger=False
FakedMonoProfilerPort=55987
```
