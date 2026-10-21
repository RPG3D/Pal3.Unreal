# CoreCLR 移动端远程调试方案

日期：2026-10-21
分支：`mobile20261021-debug`（自 `mobile20261021`）

## 先分层：诊断 ≠ 调试

CLR 移动端有两条独立的能力，成本和可行性差别很大：

| 能力 | 机制 | 需要 DAC | 移动端可行性 |
| --- | --- | --- | --- |
| 诊断：trace / counters / stack / dump / EventPipe | 运行时内置 diagnostics server + IPC | 否（分析 dump 时需要） | **可行**，Android/iOS 都支持 |
| 托管调试：断点、单步、调用栈、变量 | ICorDebug（`mscordbi` + `mscordaccore`） | **是** | Android 成熟；iOS 解释器需实测 |

诊断能解决大部分排障需求（卡顿、内存、异常、卡死），托管单步是最后一块。

## 一、诊断（第一优先级，已落地代码）

运行时在 `coreclr_initialize` 之前读取：

- `DOTNET_DiagnosticPorts=<endpoint>[,suspend][,connect]`
- `DOTNET_DefaultDiagnosticPortSuspend=1`

传输层：`src/native/eventpipe/ds-ipc-pal-socket.c` 在 Apple 平台同时支持
`AF_INET`/`AF_INET6`/`AF_UNIX`，所以移动端可以用 **TCP 端口**（配合端口转发）或应用沙盒内的
Unix socket。

打包后的 App 无法从启动环境继承变量，因此宿主在初始化前按启动参数 `setenv`：

```
-clrdiag                 # 默认 127.0.0.1:9000,suspend
-clrdiagport=9100        # 换一个空闲端口
```

代码位置：`Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost_Mobile.cpp`
（`TPA` 构建之后、`coreclr_initialize` 之前）。

### 连接方式

Android：

```bash
adb forward tcp:9000 tcp:9000
dotnet-dsrouter client-server -tcps 127.0.0.1:9001 -tcpc 127.0.0.1:9000
# 另一个终端
dotnet-trace collect --diagnostic-port 127.0.0.1:9001,connect
```

iOS（需要 libimobiledevice 的 `iproxy`，或 Xcode 的 port forwarding）：

```bash
iproxy 9000 9000 <device-udid>
dotnet-dsrouter client-server -tcps 127.0.0.1:9001 -tcpc 127.0.0.1:9000
dotnet-stack report --diagnostic-port 127.0.0.1:9001,connect
```

`dotnet-trace` / `dotnet-counters` / `dotnet-stack` / `dotnet-dump collect` 都走同一条通道。
先确认端口与 `,suspend` 行为，再做后续工具链。

## 二、托管调试（ICorDebug）

移动端的特殊约束（官方规范，见文末资料 1）：

- 不允许起独立的调试器进程，所以**调试器必须作为 profiler 组件加载进 App 进程**；
- 组件导出 `DllGetClassObject`，CoreCLR 在运行时初始化时加载它，组件再
  `dlopen(libmscordbi)` → `CoreCLRCreateCordbObjectEx` → `ICorDebug::DebugActiveProcess`；
- `libmscordbi` 和 `libmscordaccore` 必须一起打进 App（DAC 由 DBI 从自身目录查找）。

当前实现与产物：

1. **DAC + DBI 已随 SDK 分发**：`CopySDKFromSrc.sh` 保留 `mscordaccore`/`mscordbi`；
   `MakeCoreClrFramework.sh` 把它们和调试组件都放进 `CoreCLR.framework/`（DBI 与 DAC
   同目录，满足 DBI 的查找规则）。
2. **进程内组件已实现**：`CoreClrSDK/Debugger/UnrealSharpClrDebug.cpp`，由
   `Debugger/BuildClrDebugComponent.sh` 用 dotnet/runtime 的头文件编译。
3. **iOS 解释器**：断点/单步依赖运行时调试钩子；解释器对 ICorDebug 的支持弱于 JIT，
   仍需实测 attach、断点、单步、变量求值。Android 走 JIT，路径更成熟。
4. **客户端**：当前组件只做 attach 探测（进程/线程枚举、继续运行）；断点与变量前端
   需要继续在 ICorDebug 之上实现，或接入 netcoredbg 的协议层。

## 三、分阶段路线

| 阶段 | 目标 | 依赖 | 风险 |
| --- | --- | --- | --- |
| D1 | `dotnet-trace`/`dotnet-stack` 真机 attach | 已实现的端口配置 + 转发 | 低 |
| D2 | 卡死/崩溃现场：`dotnet-dump collect` + 离线分析 | 同上 + DAC（分析端） | 低-中 |
| D3 | Android 断点/单步（netcoredbg） | SDK 保留 DAC + dsrouter | 中 |
| D4 | iOS 解释器断点/单步 | DAC + 解释器调试钩子 | 高，需实测 |

## 四、当前代码状态

宿主在 `coreclr_initialize` 之前按启动参数注入环境变量（不再读 ini）：

| 参数 | 作用 | 注入的变量 |
| --- | --- | --- |
| `-clrdiag [-clrdiagport=NNNN]` | 进程外 EventPipe 诊断（默认 9000，启动挂起） | `DOTNET_DiagnosticPorts=127.0.0.1:NNNN,suspend`、`DOTNET_DefaultDiagnosticPortSuspend=1` |
| `-clrdebug [-clrdebugprofiler=<path>]` | 进程内 ICorDebug 调试 | `DOTNET_/CORECLR_ENABLE_PROFILING=1`、`DOTNET_/CORECLR_PROFILER=<CLSID>`、`DOTNET_/CORECLR_PROFILER_PATH=<组件>`、`UNREALSHARP_CLRDEBUG_{CORECLR,DBI,LOG}` |

`-clrdebug` 不填 `-clrdebugprofiler=` 时，宿主用 `dladdr(&coreclr_initialize)` 找到已加载
libcoreclr 的目录，并从那里推导组件与 DBI 的绝对路径，所以两个平台用同一条命令。

## 五、进程内调试组件（已实现）

源码与构建：

- `Source/ThirdParty/CoreClrSDK/Debugger/UnrealSharpClrDebug.cpp`
- `Source/ThirdParty/CoreClrSDK/Debugger/BuildClrDebugComponent.sh <dotnet-runtime-root> <android|ios|iossimulator> <out-dir> [arm64|x64]`
- `BuildCoreClrSDK.sh` 在拷贝完 SDK 后自动调用上面的脚本，产物直接进
  `Android/arm64/lib/` 或 `IOS/arm64/lib/`；`MakeCoreClrFramework.sh` 再把 iOS 产物放进
  `CoreCLR.framework/`。

组件行为：

1. CoreCLR 通过 profiler 机制加载组件并调用 `ICorProfilerCallback::Initialize`（发生在
   `InitializeDebugger` 之后、托管代码执行之前）；
2. 组件在独立线程里 `dlopen(libmscordbi)`，用 `CoreCLRCreateCordbObjectEx(pid=self)` 建
   Cordb，然后 `ICorDebug::Initialize` → `SetManagedHandler` → `DebugActiveProcess(pid)`；
3. `Initialize` 最多等待 15 秒（避免调试组件把游戏卡死）；成功后枚举线程并
   `ICorDebugController::Continue`，游戏继续运行；
4. 托管事件回调目前只记日志（CreateProcess / CreateThread / LoadModule / LoadAssembly /
   Breakpoint / Exception），返回 `S_OK`。

启用与查看日志：

```bash
# 设备侧命令（放进 UECommandLine.txt；Android 在 app 沙盒 files 目录，iOS 在 .app 包内）
-clrdebug
```

```bash
adb logcat -s UnrealSharpClrDebug    # Android
# iOS: Saved/Logs/ClrDebug.log（组件同时写 stderr）
```

成功时依次看到：

```
[UnrealSharpClrDebug] profiler: Initialize (starting in-process ICorDebug attach)
[UnrealSharpClrDebug] attach: CoreCLRCreateCordbObjectEx(pid=...) => hr=0x00000000
[UnrealSharpClrDebug] attach: ICorDebug::Initialize => hr=0x00000000
[UnrealSharpClrDebug] attach: ICorDebug::DebugActiveProcess(pid=...) => hr=0x00000000
[UnrealSharpClrDebug] ICorDebugController::EnumerateThreads => N thread(s)
[UnrealSharpClrDebug] attach: ICorDebugController::Continue => hr=0x00000000
```

限制：组件只验证 attach 与事件通路；断点/单步/变量求值需要在前端继续实现。
iOS 解释器下的断点仍受上游 issue 影响（见文末）。

## 已验证：Android D1（2026-10-21）

完整链路跑通，产物 `/tmp/android.nettrace`（1.35 MB），App 日志出现
`[CoreClr] coreclr_initialize => 0x0 (domain 1)`。

```bash
adb install -r Packaged/Android/SharpDemo-arm64.apk
mkdir -p /sdcard/Android/data/com.YourCompany.SharpDemo/files/UnrealGame/SharpDemo
printf -- "-clrdiag -clrdiagport=9100\n" > /tmp/uecmd.txt
adb push /tmp/uecmd.txt /sdcard/Android/data/com.YourCompany.SharpDemo/files/UnrealGame/SharpDemo/UECommandLine.txt

dotnet-dsrouter server-server -tcps 127.0.0.1:9100 -fp Android
adb reverse tcp:9100 tcp:9100          # 见下方坑 2
adb shell monkey -p com.YourCompany.SharpDemo -c android.intent.category.LAUNCHER 1

dotnet-trace collect --diagnostic-port "<router 日志里的 socket>,connect" \
    --duration 00:00:05 -o out.nettrace
```

两个必须记住的坑：

1. **宿主 9000 可能被别的进程占用**（本机是 node）。用 `-clrdiagport=9100` 选一个空闲端口，并用 `lsof -iTCP:9100` 确认。
2. **`dotnet-dsrouter -fp Android` 自动建的是设备 9099 -> 宿主 9100 的映射**，而运行时连的是
   `127.0.0.1:9100`（设备侧）。必须额外执行 `adb reverse tcp:9100 tcp:9100`，否则 router 永远收不到
   runtime 连接（表现为一直 suspend，router 日志里只有 `Broken TCP connection`）。

工具版本：`dotnet-dsrouter` / `dotnet-trace` 10.0.750501 与 11.0 rc 运行时可以正常通信；
但 `dotnet-stack` 10.0 已不支持 `--diagnostic-port`，托管栈请用 trace 的 sample profiler 或 netcoredbg。

## 六、ICorDebug 进程内 attach 实测（Android，2026-10-22）

已跑通的层级（`-clrdebug`，日志 tag `UnrealSharpClrDebug`）：

```
component: loaded into pid N
profiler: DllGetClassObject(...)
profiler: class factory created the callback object
profiler: Initialize (component loaded; attach is host-driven)
attach: libmscordbi DllMain(DLL_PROCESS_ATTACH) => 1
attach: CoreCLRCreateCordbObjectEx(pid=N) => hr=0x0
attach: QI(ICorDebug) => hr=0x0
attach: ICorDebug::Initialize => hr=0x0
attach: ICorDebug::SetManagedHandler => hr=0x0
attach: DebugActiveProcess(pid=N) => hr=0x0, process=...
attach: ICorDebugProcess::GetID => hr=0x0
callback: CreateProcess(process=..., appdomain=...)
```

**已经 attach 成功，并收到了托管的 CreateProcess 事件。** 过程中的四个关键结论
（都已修入组件）：

1. **组件必须静态链接 libc++**（`-static-libstdc++`）。UE 的 APK 不带
   `libc++_shared.so`，否则 CoreCLR 的 `dlopen` 直接失败，profiler 根本加载不进来。
2. **普通（非 universal）libmscordbi 需要宿主调用它导出的 `DllMain(DLL_PROCESS_ATTACH)`**。
   否则 PAL 未初始化，`ICorDebug::Initialize` 内的第一次 `CreateEvent` 会永久阻塞
   （上游 issue #125976 描述的正是同一类 PAL 初始化问题）。desktop 上 dbgshim 做的就是这一步。
3. **Android 没有 `/tmp`**。调试传输的 Unix socket/FIFO 路径来自 `GetTempPath`，
   必须把 `TMPDIR` 指到 app 内部 cache（`AndroidThunkCpp_GetCacheDir()`，路径够短，
   `sockaddr_un` 限长 108）。指向 `Saved/Temp` 这种长路径会让运行时侧直接建 FIFO 失败。
4. **`CoreCLRCreateCordbObject*` 的 `hmodTargetCLR` 不是加载器句柄，而是 CLR 实例 ID，
   即 libcoreclr 的加载基址**（dbgshim 从目标进程的 version string 里解析它）。
   传 `dlopen` 返回值会让 `OpenVirtualProcess` 找不到 CLR 实例，`DebugActiveProcess`
   返回 `CORDBG_E_OBJECT_NEUTERED`。组件现在用
   `dladdr(dlsym(target, "coreclr_initialize"))->dli_fbase` 取基址。

组件内置的诊断钩子（真机排障用）：

- `attach: TMPDIR=...`
- `attach: target CLR base=...`
- `attach: DAC pipe names: in=... out=...`（直接调用 DAC 的
  `DAC_PAL_GetTransportPipeName`，与运行时实际创建的 FIFO 名对比）
- `attach: runtime transport is listening ...`（对 FIFO 做 `O_WRONLY|O_NONBLOCK` 探测）
- 8 秒看门狗：卡住时用 `_Unwind_Backtrace` 取调用线程栈

**当前剩余卡点**：attach 成功后调用 `ICorDebugController::Continue` 会阻塞
（`CreateProcess` 回调已正常返回，Continue 的 IPC 请求等待运行时侧响应）。
游戏线程因此停在 `UnrealSharpClrDebugWaitForAttach` 之后的创建托管委托之前。
下一步需要确认是 in-process 下 LS 等待事件 ack 的顺序问题，还是必须在
`CreateProcess` 回调内/回调后按特定顺序 Continue。

## 待办

- Android：定位 `Continue` 阻塞（事件 ack 顺序 / Stop-Go 锁）。
- 断点/单步/变量：在 attach 打通后接 ICorDebug 前端。
- D2：`dotnet-dump collect` 走同一条链路。
- D3：netcoredbg attach（同一 socket）。
- iOS：换 `iproxy`；DAC/DBI/组件已随 framework 打包，需用最新组件重打一次。

## 关键资料（官方）

### 1. 移动端自定义调试器初始化（.NET 11+）

https://learn.microsoft.com/en-us/dotnet/core/unmanaged-api/debugging/initialize-debugger-mobile

核心结论（官方规范，适用于 iOS/Android，不含 NativeAOT）：

- 移动平台不允许调试器独立进程，因此目标侧调试器必须是**加载进 App 进程的 profiler 组件**
  （导出 `DllGetClassObject`）；
- 随 App 部署 `libmscordbi` + `libmscordaccore`；
- 启动前设置（`DOTNET_*` 与 `CORECLR_*` 两种前缀都建议设）：

```
DOTNET_ENABLE_PROFILING=1
DOTNET_PROFILER={<CLSID>}
DOTNET_PROFILER_PATH=<component path>
```

- 组件内：加载 `libmscordbi` → `CoreCLRCreateCordbObjectEx` → `ICorDebug` →
  `Initialize` / `SetManagedHandler` / `DebugActiveProcess`；
- 必须在托管代码开始执行前完成 attach。

本地验证：Android debug 构建的 `libmscordbi.so` 已导出
`CoreCLRCreateCordbObjectEx` / `CoreCLRCreateCordbObject3`，`libcoreclr.so` 也包含 profiler 加载逻辑。
因此我们走的是进程内组件路线，不需要把 netcoredbg 作为独立进程移植到 Bionic。

### 2. 上游 tracking issue

https://github.com/dotnet/runtime/issues/124744
`[clr-ios] Enable debugging on CoreCLR mobile platforms`

DoD：VS Code F5 启动 iOS App、自动 attach、命中断点。任务分三块：

1. 把调试器组件编译并链接进 app bundle（remote debugging on devices）；
2. VS Code inner developer loop；
3. 诊断集成：iOS 崩溃报告带托管栈、关联 IL offset 与方法元数据，并与 Android
   CoreCLR crash diagnostics 对齐。

相关子问题（iOS，说明解释器调试仍在修复中）：

- #125974 Copy post-link assemblies and include DAC/DBI libraries for iOS remote debugger
- #126020 Interpreter cannot set breakpoints in already-interpreted methods
- #125975 Debugger shows wrong source file and variables when stopped in interpreted code
- #125976 PAL CreateThread crashes when initialized with PAL_InitializeDLL
- #125958 Debugger crashes with SIGSEGV in GetFuncletStartOffsets
- #125959 FuncEval does not work on iOS because executable memory allocation fails
- #126955 / #127480 / #126953 异常拦截、断点后解释器上下文、DAC/DBI 栈帧重复

对我们的含义：

- #125974 正是我们已经完成的 DAC/DBI 随包分发；
- iOS 解释器下的断点/变量/FuncEval 仍有未修复的上游缺陷，D3 应优先在 Android 上验证；
- 宿主侧 `-clrdebug -clrdebugprofiler=<path>` 与 `-clrdiag [-clrdiagport=]` 已按
  “调试器 / 诊断”拆分，前者只注入 profiler 变量，后者才开诊断端口。
