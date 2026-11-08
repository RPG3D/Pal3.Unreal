# 会话状态速查（2026-10-21 深夜）

这是当时的移动端运行时快照。当前 PAL3 的构建入口、运行时配置和验证范围见[项目 README](../../README.md)。

## 分支与提交（均本地，未 push）

| 仓库 | 分支/提交 | 内容 |
| --- | --- | --- |
| 插件 | `mobile20261021` @ `28a32f17` | CoreCLR 主线（阶段一移植完成） |
| 插件 | `mobile20261021-debug` | 远程诊断/调试（`-clrdebug` / `-clrdiag`，宿主已拆分） |
| 插件 | `preparemobile` | 上游 PR 素材（生成器 ABI twin、glue 守卫） |
| 父仓库 | `main` | 文档与子模块指针；最新文档提交 `a37fb81` |
| CoreClrSDK | 本地未提交 | 两段式构建、DAC+DBI、去冗余 |
| 本地 dotnet/runtime | 未提交 | `release/11.0`（3beced8c）+ pkg-config/openssl 警告化补丁（本机环境绕过） |

## SDK 现状

- 本地构建的 Android/iOS debug SDK：`coreclr + 引擎 + System.* + libmscordaccore + libmscordbi`；
- Android `lib/` 7 个文件，`runtime/` 181 个 DLL（无 pdb/xml）；iOS framework 二进制旁为 interpreter + DAC + DBI（无 JIT）；
- 备份：`IOS/arm64.pre-dac-backup`、`Android/arm64.pre-dac-backup`；
- 构建：`BuildCoreClrSDK.sh` 两段式（正常构建 + `-component debug`），`CopySDKFromSrc.sh` 无条件拷 DAC/DBI。

## 调试流程

- 调试器：`-clrdebug -clrdebugprofiler=<abs path>` → 设 `DOTNET_/CORECLR_` profiler 变量（组件待实现）；
- 诊断：`-clrdiag -clrdiagport=9100` → `DOTNET_DiagnosticPorts=127.0.0.1:9100,suspend`；
- 已验证 D1（Android）：dsrouter `server-server -tcps 127.0.0.1:9100 -fp Android` + 手动 `adb reverse tcp:9100 tcp:9100` + `dotnet-trace`；
- 两个坑：宿主 9000 被 node 占用（用 9100）；dsrouter 自动映射设备 9099，必须补 9100→9100。

## 关键资料

- https://learn.microsoft.com/en-us/dotnet/core/unmanaged-api/debugging/initialize-debugger-mobile
- https://github.com/dotnet/runtime/issues/124744 （#125974 = DAC/DBI 随包分发，已完成）

## 待办

1. 打包结果核对（Android APK / iOS app 含 mscordbi、签名、无冗余）；
2. 实现 profiler 组件最小探针（`DllGetClassObject` + `CoreCLRCreateCordbObjectEx`）；
3. D2 备选：`dotnet-gcdump`（不需要 createdump）；
4. iOS 设备验证（iPad 解锁后）；Android D1 已通过。

## 固定坑

- 换 framework zip 后清：`UnzippedFrameworks/CoreCLR`、`Saved/StagedBuilds/IOS`、`Binaries/IOS/.../CoreCLR.framework`、`Packaged/IOS/SharpDemo.app`；
- 缺少 `Script/Pal3.Core/Libraries` 的 DLL 会让解决方案发布失败（本地已补）；
- `Config/IOS/IOSEngine.ini` 是签名配置，不要删。
