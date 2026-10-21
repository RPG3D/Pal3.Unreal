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

打包后的 App 无法从启动环境继承变量，因此宿主在初始化前从项目配置读取并 `setenv`：

```ini
[UnrealSharp]
; TCP 端点，配合 adb forward / iproxy 使用；加 ,suspend 可让工具在托管代码执行前接管
DiagnosticPorts=127.0.0.1:9000,suspend
bDiagnosticPortSuspend=True
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

链路：

```
netcoredbg / vsdbg  ──TCP──  dotnet-dsrouter  ──TCP/socket──  App 内 diagnostics server
                              │
                              └── 需要与运行时完全匹配的 mscordaccore（DAC）
```

现状与缺口：

1. **DAC 没有随 SDK 分发**。`CoreClrSDK/CopySDKFromSrc.sh` 明确丢弃
   `mscordaccore`/`mscordbi`（它们只用于调试，体积大）。要启用 ICorDebug，需要在 SDK
   构建里保留 DAC（Android 的 `.so` / iOS 的 dylib），并按运行时版本严格匹配。
2. **iOS 解释器**。断点/单步依赖运行时调试钩子；解释器对 ICorDebug 的支持弱于 JIT，
   需要实测 attach、断点、单步、变量求值。Android 走 JIT，路径成熟。
3. **客户端**。`netcoredbg`（Samsung，开源）支持 ICorDebug，可作为首选；`vsdbg` 闭源且绑定
   VS/VSCode。

建议先做 Android（JIT + DAC，成功率高），再评估 iOS 解释器的调试能力。

## 三、分阶段路线

| 阶段 | 目标 | 依赖 | 风险 |
| --- | --- | --- | --- |
| D1 | `dotnet-trace`/`dotnet-stack` 真机 attach | 已实现的端口配置 + 转发 | 低 |
| D2 | 卡死/崩溃现场：`dotnet-dump collect` + 离线分析 | 同上 + DAC（分析端） | 低-中 |
| D3 | Android 断点/单步（netcoredbg） | SDK 保留 DAC + dsrouter | 中 |
| D4 | iOS 解释器断点/单步 | DAC + 解释器调试钩子 | 高，需实测 |

## 四、当前代码状态

- 宿主已支持从 `[UnrealSharp]` 读取 `DiagnosticPorts` / `bDiagnosticPortSuspend`，并在
  `coreclr_initialize` 前设置环境变量。
- 未做真机验证（iPad 在使用、Android 设备未连接）；下一步是 D1 的端到端 attach。
- DAC 尚未加入 SDK，D3/D4 需要先补构建产物。

## 已验证：Android D1（2026-10-21）

完整链路跑通，产物 `/tmp/android.nettrace`（1.35 MB），App 日志出现
`[CoreClr] coreclr_initialize => 0x0 (domain 1)`。

```bash
adb install -r Packaged/Android/SharpDemo-arm64.apk
mkdir -p /sdcard/Android/data/com.YourCompany.SharpDemo/files/UnrealGame/SharpDemo
printf -- "-clrdebug -clrdebugport=9100\n" > /tmp/uecmd.txt
adb push /tmp/uecmd.txt /sdcard/Android/data/com.YourCompany.SharpDemo/files/UnrealGame/SharpDemo/UECommandLine.txt

dotnet-dsrouter server-server -tcps 127.0.0.1:9100 -fp Android
adb reverse tcp:9100 tcp:9100          # 见下方坑 2
adb shell monkey -p com.YourCompany.SharpDemo -c android.intent.category.LAUNCHER 1

dotnet-trace collect --diagnostic-port "<router 日志里的 socket>,connect" \
    --duration 00:00:05 -o out.nettrace
```

两个必须记住的坑：

1. **宿主 9000 可能被别的进程占用**（本机是 node）。用 `-clrdebugport=9100` 选一个空闲端口，并用 `lsof -iTCP:9100` 确认。
2. **`dotnet-dsrouter -fp Android` 自动建的是设备 9099 -> 宿主 9100 的映射**，而运行时连的是
   `127.0.0.1:9100`（设备侧）。必须额外执行 `adb reverse tcp:9100 tcp:9100`，否则 router 永远收不到
   runtime 连接（表现为一直 suspend，router 日志里只有 `Broken TCP connection`）。

工具版本：`dotnet-dsrouter` / `dotnet-trace` 10.0.750501 与 11.0 rc 运行时可以正常通信；
但 `dotnet-stack` 10.0 已不支持 `--diagnostic-port`，托管栈请用 trace 的 sample profiler 或 netcoredbg。

## 待办

- D2：`dotnet-dump collect` 走同一条链路。
- D3：netcoredbg attach（同一 socket）。
- iOS：把 `adb reverse` 换成 `iproxy`，其余流程相同；DAC 已在本地 debug 包中。
