# 诊断与监控方案（C++ / C#，卡死与崩溃）

> 目标：程序卡死或崩溃时，**能拿到"卡在哪、崩在哪、当时在干什么"**，而不是只看到窗口不动。
> 结论先说：**C++ 侧 UE 内置已经够用，不需要第三方库**；**C# 侧 Mono 什么也没有，已在本仓库补了一个自研轻量模块**（`Script/Engine/Diagnostics/ManagedDiagnostics.cs`）。远程聚合（多用户/多设备上报）才需要考虑 Crashpad/Sentry，那是后期的事。

---

## 1. 现状矩阵

| 故障形态 | UE (C++) 内置能力 | 现状 | C# (Mono) 内置能力 | 本仓库补充 |
|---|---|---|---|---|
| 访问违例 / assert | `CrashReportClient` + `Saved/Crashes/<GUID>/`（minidump + `CrashContext.runtime-xml`，含调用栈、模块） | ✅ 已在用（本次 coreclr AV 就是从 `Saved/Crashes` + Windows 事件日志定位的） | 无 | — |
| 卡死（GameThread 无心跳） | `FThreadHeartBeat` + `USE_HANG_DETECTION`（**仅打包版**：`USE_HANG_DETECTION` 排除了 editor，`Build.h:447`）；开关 `-debughangdetection` | ⚠️ PIE/编辑器里**不生效** | 无 | ✅ 托管看门狗（`PAL3_WATCHDOG_SECONDS`，默认 20s） |
| 卡在某个脚本 Update | 无（UE 看不到托管帧） | ❌ | 无 | ✅ 单脚本耗时 >100ms 记 `SLOW` |
| 卡在协程 | 无 | ❌ | 无 | ✅ `CoroutineScheduler.Tick` 总耗时 >100ms 记 `SLOW` |
| 托管未捕获异常 | 无 | ❌ | `AppDomain.UnhandledException` 可挂 | ✅ 已挂，写文件 + UE 日志 |
| 后台任务异常被吞 | 无 | ❌ | `TaskScheduler.UnobservedTaskException` | ✅ 已挂 |
| 被吞掉的异常（first-chance） | 无 | ❌ | `FirstChanceException`（需开关） | ✅ `PAL3_TRACE_EXCEPTIONS=1`，1/200 节流 |
| 性能/帧时间 | Unreal Insights、`stat unit` | ✅ 可用 | 无 | 心跳里带帧计数与耗时 |

---

## 2. 已实现：`ManagedDiagnostics`

位置：`Script/Engine/Diagnostics/ManagedDiagnostics.cs`，由 `APal3Driver.BeginPlay` 安装、`Tick` 每帧心跳。

### 开关（环境变量）

| 变量 | 默认 | 作用 |
|---|---|---|
| `PAL3_WATCHDOG_SECONDS` | 20 | 主循环无心跳多少秒判定为卡死 |
| `PAL3_DIAG_DIR` | `./Saved/Logs` | 报告目录 |
| `PAL3_TRACE_EXCEPTIONS` | 关 | `1` = 记录 first-chance 异常（1/200 节流，排查"被吞异常"用） |
| `PAL3_TRACE_UPDATES` | 关 | `1` = 逐脚本打印 `[TickTrace]`，用于定位"崩在哪个 Update"（native AV 只有它能定位） |

### 产物

```
Saved/Logs/managed-diag-YYYYMMDD.log        # 追加写，进程硬崩也保留
[Diag] HANG no frame for 25.0s (phase=..., heartbeat=..., uptime=...)
[Diag] SLOW SomeController.Update took 812ms
[Diag] UNHANDLED System.NullReferenceException ...
```

> 为什么还要写文件：托管异常/卡死发生时 UE 日志可能还没来得及 flush 到磁盘；独立文件是唯一可靠的证据留存。

### 已接入的探针

- `GameEntityScriptRuntime.TickAll`：每个脚本 `Update` 计时，>100ms 报 `SLOW`（能直接指到类名）；
- `CoroutineScheduler.Tick`：整体计时，>100ms 报 `SLOW`；
- `APal3Driver.Tick`：每 2s 打印 `actors/scripts/coroutines/meshes/pawn/bounds` 心跳，卡死前后对比最后一行即可知道世界状态。

---

## 3. C++ 侧建议用法（不引入库）

```bash
# 1) 卡死检测（打包版；编辑器里被编译掉）
MyGame.exe -debughangdetection -log

# 2) 崩溃现场
#    Saved/Crashes/<GUID>/CrashContext.runtime-xml  ← ErrorMessage/CallStack/模块
#    Saved/Crashes/<GUID>/UEMinidump.dmp            ← 可用 VS/WinDbg 打开

# 3) 定位是否为托管崩溃
#    Windows 事件查看器：Application 日志 → UnrealEditor.exe 的 APPCRASH，
#    P4=coreclr.dll / P7=c0000005 → 崩溃发生在托管/互操作层
```

补充手段（都在引擎内，无需三方库）：
- `-abslog=<path>`：每次运行独立日志，避免"旧进程占用日志导致读到旧内容"（这次踩过）；
- `Unreal Insights`（`-trace=cpu,frame,gpu`）：卡顿/耗时归因；
- `stat unit` / `stat game`：帧时间分解。

---

## 4. 什么时候才需要第三方库

| 需求 | 建议 |
|---|---|
| 本地开发定位（当前阶段） | **不需要**。UE 内置 + 本模块已覆盖 |
| 远程/多设备自动上报、符号化、聚合看板 | `Crashpad`（Google，跨平台，minidump 上传）或 `Sentry`（`sentry-native` + `sentry-dotnet`，托管/原生通吃，带 release 追踪） |
| 只想要 native 栈符号化 | `dbghelp`/`SymFromAddr`（Windows 内置）+ UE 的 `UnrealSymbolizer` |
| Android 真机崩溃 | 系统 `tombstone`（`adb shell dumpsys dropbox`）+ `logcat -b crash`；UE Android 侧走 `libcrashreporter` |

**建议**：先把"能定位"做扎实（本次已做），等到需要**线上用户**崩溃收集时再上 Sentry/Crashpad——那时它俩的接入成本远低于现在维护一个自研上报链路。

---

## 5. 本次定位实践（可作为流程模板）

1. 现象：进程消失、日志截断、无 `LogExit`；
2. 查 `Saved/Crashes/*`：无新目录 → 说明不是 UE 捕获的崩溃；
3. 查 Windows 事件日志 → `P4: coreclr.dll, P7: c0000005` → 托管/互操作层 AV；
4. 对照日志最后一行（`Script [1511] finished running`）+ 当时刚激活的系统（12 个 `Mv3ModelRenderer`）→ 锁定"首次运行角色 Update"这条新路径；
5. 静态审查该路径的 native 调用（`UETransform` 未校验 `IsValid`、`Destroy` 不级联）→ 修复；
6. 仍未复现时：`PAL3_TRACE_UPDATES=1` 逐脚本追踪，最后一行即元凶。
