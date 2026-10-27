# DotNetRuntimeSDK 统一 + iOS CoreCLR(dotnet11) 计划

> 状态：**计划稿，待讨论**（2026-10-28 起）
> 本文只记录结论与待办，不记录探索过程；过程见 `android-runtime-matrix-mac`、`CLR-REMOTE-DEBUG.md`。

## 0. 一句话结论

把 `MonoSDK` 与 `CoreClrSDK` 的**插件侧代码**（Build.cs / staging / 版本表）收成一份 `DotNetRuntimeSDK`，二进制仓库暂不合并；iOS CoreCLR 只走 dotnet 11（10 的 iOS CoreCLR 明确放弃）。顺序：先做一个便宜的兼容性判定实验，再统一，最后开 11 分支。

## 1. 已确定的决策

### 1.1 版本矩阵不是全局常量

| backend × platform | runtime 版本 | 依据 |
| --- | --- | --- |
| Mono × 全部平台 | **10** | Mono 冻结在 10：.NET 11 SDK 拒绝 Mono target（NETSDK1242） |
| CoreCLR × Android | **10**（当前 10.0.12） | 已通过 Android 四组合真机验证 |
| CoreCLR × iOS | **11** | 10 的 iOS CoreCLR 默认不开解释器；自己开要持续踩 SDK 缺陷 |

统一方案必须把「版本」建模成 `(backend, platform) → tag` 的映射，且全项目只有一份：

- `Build.cs` 用它校验载荷存在与版本匹配；
- fetch 脚本用它决定下载哪份；
- CI 用它决定构建哪份。

### 1.2 iOS CoreCLR 放弃 dotnet 10

iOS 侧目标形态是 **AOT BCL + 解释器**。dotnet 10 的 iOS CoreCLR 默认不带解释器，自建要改 SDK 内部开关，成本与风险都不可控。10 上实测的失败点（仅作记录，不再追）：

```
dyld: Library not loaded: @rpath/libclrinterpreter.dylib
  Referenced from: .../SharpDemo.app/Frameworks/CoreCLR.framework/CoreCLR
```

原因已定位：`CoreCLR.framework/CoreCLR` 的 `LC_RPATH` 只有构建机绝对路径（`.../ios.arm64.Release/interpreter`），而 `libclrinterpreter.dylib` 的 install name 是 `@rpath/CoreCLR.framework/libclrinterpreter.dylib`。`MakeCoreClrFramework.sh` 目前只改 `install_name`，不写 `LC_RPATH`（11 的官方产物自带 `@loader_path`，所以历史上没暴露）。**C 阶段需要处理这一点。**

### 1.3 二进制仓库暂不合并

Mono 载荷数百 MB、CoreCLR 百 MB 级，合成单一 release 只会拖慢 CI 与下载；维护成本大头在插件侧代码，不在仓库结构。统一的是「客户端代码 + 版本表」。

## 2. 统一方案（B 阶段）结构

```
Source/ThirdParty/DotNetRuntimeSDK/
├── DotNetRuntimeSDK.Build.cs   # 唯一入口：resolver + staging + 链接
├── RuntimeBackend.cs           # UnrealSharpRuntimeSelection（从 MonoSDK.Build.cs 迁出）
├── RuntimeVersions.cs          # (backend, platform) -> tag 的唯一表
├── Mono/<Platform>/<arch>/     # 载荷（git-ignored）
├── CoreCLR/<Platform>/<arch>/  # 载荷（git-ignored）
├── FetchRuntimeSDK.sh          # 单入口拉取（按 backend+platform+tag）
└── *.APL.xml                   # Android 侧 APL（每个 backend 一份）
```

要点：

1. **resolver 独立**。当前 `UnrealSharpRuntimeSelection` 寄生在 `MonoSDK.Build.cs`，`CoreClrSDK.Build.cs` 反向依赖它——这是设计异味，也是「改一处漏一处」的温床。
2. **staging 只有一份实现**。BCL → `Content/Managed/<Platform>`、项目 DLL/PDB/JSON、APL/receipt 挂接，全走同一组函数。今天两个 `Build.cs` 各写一遍路径拼装，直接用出了事故：`CoreClrSDK.Build.cs` 在 layout 改成 `<Platform>/<arch>/` 后仍引用 `Android/lib`，导致 `libcoreclr.so` 从未进入链接行（`e6b3be0` 修复）。
3. **过渡期用薄壳**。新模块先接管 resolver + staging，`MonoSDK` / `CoreClrSDK` 退化为只声明库路径的 shim；六组合回归通过后再删壳。
4. **iOS CoreCLR 在 C 落地前不产出载荷**，避免一份跑不起来的 SDK 被误用。

## 3. 阶段与判定标准

| 阶段 | 内容 | 判定标准 | 预估 |
| --- | --- | --- | --- |
| **A** | `CoreClrDemo/IOSClrDemo`：runtime 11 × managed **net10** vs runtime 11 × managed **net11** | 逐项过：`coreclr_initialize` → 托管入口 → P/Invoke（System.Native）→ **泛型/反射实例化**（解释器 + AOT BCL 下最易缺）→ 线程/异常。第一格通过 = Sharp 可留 net10；不通过 = C 范围翻倍（托管层也要升 11） | 0.5 天 |
| **B** | 统一 SDK（10 基线，iOS CLR 不产出） | Android A/B/C/D 四组合 + iOS 两组（编辑器 Mono/CoreCLR × iOS Mono）全部真机通过 | 1~1.5 天 |
| **C** | 开 11 分支：iOS CoreCLR（换 11 载荷 + framework rpath/install_name 处理） | iOS 真机启动 + 托管入口 + 玩法可跑 | 2~3 天 |

**为什么 A 先行**：A 的结论决定 C 是「只换载荷」还是「托管层整体升级」，两者工作量差数倍；而 A 不需要 UE 打包，成本极低。
**为什么 B 在 C 之前**：C 要改的正是 SDK 脚本与打包逻辑；在两份重复代码上并行修改，等于把今天的 arch 路径事故复制一遍。

## 4. 当前基础设施状态（2026-10-28）

### 4.1 真机验证矩阵（全部通过）

| 组合 | 平台 | 结果 |
| --- | --- | --- |
| A 编辑器 Mono × Target Mono | Android（小米 24091RPADC） | ✅ |
| B 编辑器 Mono × Target CoreCLR | Android | ✅ `coreclr_initialize => 0x0` |
| C 编辑器 CoreCLR × Target Mono | Android | ✅ |
| D 编辑器 CoreCLR × Target CoreCLR | Android | ✅ |
| 编辑器 Mono × iOS Mono | iPad air 7 | ✅ |
| 编辑器 CoreCLR × iOS Mono | iPad air 7 | ✅ |

打包脚本：`build-android-mac.sh <A|B|C|D>`、`build-ios-mac.sh <editorRuntime> [targetRuntime]`。

### 4.2 已合入 main 的关键修复

| 仓库 | 提交 | 内容 |
| --- | --- | --- |
| CoreClrSDK | `e6b3be0` | 载荷路径补 `<arch>` 层（`Android/arm64`、`IOS/arm64`）——B/D 组合前提 |
| CoreClrSDK | `af5a066` | runtime 钉到 `v10.0.12`（与 Win11 NuGet 验证过的版本一致） |
| CoreClrSDK | `54eaeaa` | 构建脚本：iOS 补丁在 10 分支无匹配时跳过（`pipefail` 下会静默退出）；有 ninja 时钉 `-ninja`，避免 CMake generator 冲突 |
| CoreClrSDK | `9dd7d87` | `FetchCoreClrSDK.sh` GitHub Releases 单一来源（禁用 NuGet） |
| MonoSDK | `30ca4cc` | 分层 `DotNetRuntime` resolver + 把 engine ini 注册为 `ExternalDependencies` |
| UnrealSharp | `d685672d` | iOS 覆盖目录清理修复（见 4.3） |
| UnrealSharp | `a7db3c3a` | 两个 SDK gitlink 对齐各自 main |

CI：MonoSDK 全平台 **success**；CoreClrSDK Android 22m46s ✅、iOS Simulator 21m36s ✅、iOS device 仍在跑（脚本修复后的新 run `40550066856`）。

### 4.3 iOS 覆盖目录清理（已修，附背景）

`PurgeStaleOverrideDlls` 用可执行文件 mtime 判定 `Saved/Managed/<Platform>` 里的覆盖 DLL 是否过期，但走的是 `IPlatformFile`（顶层是 Pak 层，pak 外文件一律 invalid），在 iOS 上直接 return，导致**陈旧 CoreLib 永远不被清理**，最终 Mono 报：

```
Failed to load AOT module 'System.Private.CoreLib' while running in aot-only mode: doesn't match assembly.
```

修复方式与 UE 自身 `FPipelineFileCacheManager::ClearOSPipelineCache` 一致：iOS 直接 POSIX `stat()`；其余平台保持原逻辑；完全拿不到时间戳时**全部视为陈旧**（不能证明比包内新就不该赢）。已在 iPad 上用「注入 mtime=2020 的假 CoreLib」验证清理生效。

## 5. 待办（按优先级）

### 5.1 立即（今天余下，均为小改动）

1. **glue 重生成丢 Niagara tags 治理**。每次移动端打包 `PackageProjectMobile` 会重生成 `Script/SharpDemo.RuntimeGlue/*.cs`，把 `GameplayTags.cs` 里的 Niagara 系列 tag 抹掉（已发生并被手工恢复过一次）。打包工具不应改写受版本控制的源码。修法二选一：commandlet 上下文跳过重生成，或让生成器按编辑器实际加载模块集幂等输出。
2. **SDK 版本钉 tag**。MonoSDK CI 现在克隆 `release/10.0` **分支尖端**（会漂移），CoreClrSDK 钉 tag——同一 commit 在不同日子产出不同 SDK，直接影响设备结论的可归因性。统一为 tag（MonoSDK → `v10.0.13`）。
3. **清理 iOS 的 10.0 CoreCLR 载荷**（`CoreClrSDK/IOS/arm64`），或改名留档，避免 C 阶段前被误用。

### 5.2 之后

4. A 阶段实验 → 结论写入本文。
5. B 阶段统一（含六组合回归）。
6. C 阶段 11 分支。
7. 未结清的旧账：Mac 编辑器 Mono 调试崩溃（F5 被信号拦截、F6 可继续，疑 `MONO_THREADS_SUSPEND=preemptive` 信号）；`-MonoDebugSymbols` / `mono_debug_paths.json` 是否冗余；MonoSDK Debug 产物 + `.dSYM` 拷贝 + README 调试章节。

## 6. 风险

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| A 判定不通过（runtime11 跑不了 net10 程序集） | C 范围翻倍，托管层要整体升 11，且牵动 Mono 侧构建链 | A 先做；判定标准包含泛型/反射，避免"启动即通过"的假正 |
| 统一期间回归面广 | 六组合都要重验，容易停在中间态 | 薄壳过渡 + 每步跑完整矩阵再删壳 |
| iOS framework rpath 在 11 也需要手写 | C 阶段仍要动 `MakeCoreClrFramework.sh` | 已在本文记录 `LC_RPATH` 处理点 |
| 两套 SDK 版本继续漂移 | 设备结论不可复现 | 5.1 第 2 项 |

## 7. 参考

- `Docs/rebuild/CLR-REMOTE-DEBUG.md`、`Docs/rebuild/STAGE1-CORECLR-PORT.md`
- `build-android-mac.sh`、`build-ios-mac.sh`（项目根）
- `CoreClrDemo/IOSClrDemo`（A 阶段实验载体）
