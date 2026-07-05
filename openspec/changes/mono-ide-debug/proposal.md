# Proposal: Mono IDE Debug Support

## Problem

Mono 集成完成后，C# 代码可以在 Unreal Engine 中运行，但无法通过 IDE (Visual Studio 2022 / JetBrains Rider) 设置断点、单步调试或查看变量。开发和调试 C# 脚本只能依赖 `UE_LOG` 日志输出，效率极低。

## Solution

集成 Mono 内置的 **Soft Debugger Agent**，通过 `mono_jit_parse_options("--debugger-agent=...")` + `mono_debug_init()` 在 Editor 启动时自动启动。支持 VS 2022（通过 Visual Studio Tools for Unity 兼容层）和 Rider（通过标准 Mono Remote Debugger 协议）。

## Key Design Points

- **Dual-agent 架构**: 同时启动 VS 和 Rider 两个 debugger agent，无需重启 Editor 切换 IDE
- **JIT 模式**: 不使用 INTERP_ONLY（会破坏 `MethodHandle.GetFunctionPointer()` 导致 Blueprint method handle 解析失败）
- **VS 兼容性 hack**: 创建 `{ExeName}_Data/` 目录 + `UnityPlayer.dll` 伪装 Unity 进程；假 TCP Profiler listener 满足新版 VSTU 要求
- **端口策略**: VS 使用 `56000 + pid % 1000`（VSTU 硬编码公式），Rider 使用固定端口 57000
- **Shipping 排除**: 完全通过 `#if !UE_BUILD_SHIPPING` 排除

## Scope

- **In**: Win64 Editor 下 VS 2022 和 Rider 的断点/单步/变量查看调试 ← 主流程已通
- **Out**: Android/iOS 远程调试（后续）
- **Out**: 展开变量时 native 调用崩溃问题 ← **当前阻塞项**

## References

- [bodong1987/UnrealSharp](https://github.com/bodong1987/UnrealSharp) — `MonoRuntime.cpp:InitDebugger()`
- [cnblogs: VS Tools for Unity 兼容](https://www.cnblogs.com/bodong/p/17962564) — 进程识别机制 + 端口公式
