# PR #795 与移动打包流程研究（2026-11-22）

## 结论

值得借鉴它的 **UAT Stage 扩展入口、UBT 回执驱动、在标准打包流水线内发布** 三点；不建议合并整个 PR，也不能只删除它的 Android/iOS 平台限制后使用。

严格说，PR 的主体不是“UBT 插件”：`CustomStagingHandler` 属于 AutomationTool，UBT 提供目标回执。我们可把原生构建、托管发布、Stage 的职责收敛到这条官方扩展链，减少当前几份脚本对构建顺序的重复维护。移动 Mono/CoreCLR、真实 SDK/BCL、多游戏清单和整套事务必须保留自己的实现。

本篇只研究，没有安装 handler、复制 PR 实现或更改现有打包入口。九项审查修复见 `PAL3A_FIX_AND_REVIEW_2026-11-22.md`。

## 上游快照与实际能力

读取了 [PR #795](https://github.com/UnrealSharp/UnrealSharp/pull/795) 的描述、完整 diff、12 个变更文件和讨论。2026-11-22 查询时仍为 open、未合并；研究固定 head `f3c0c3680cbe6b4b4850c809cc06a4428362a9fa`，base `947b6ae5a32542ef9be5588158b8ea5432e6f6a2`，避免后续 PR 更新改变结论。

| 上游内容 | 实际行为 | 对本项目的意义 |
|---|---|---|
| CustomStagingHandler | UE 5.7+ 在正常 Stage 中发现并调用；先检查目标回执是否使用 UnrealSharp | 保留标准 UE Package/BuildCookRun 入口，不另造一套打包 UI |
| UnrealSharpStaging | 从 receipt 取得平台、配置、目标类型和单一架构，核对项目及插件来源 | 优于脚本和手工 ini 推断目标；可消除本轮构建计划中的部分重复解析 |
| 内部调用 PackageProject | 在 UAT 进程中直接创建命令并发布，再用 SC.StageFiles 注册 NonUFS 文件 | 可以避免“先打包、再向归档补 DLL”；不等于允许再启动一个 UAT |
| 平台/目标限制 | 明确只允许 Win64/Mac；拒绝 DLC、同目录多目标和 Mac universal | 该 PR 没有给出移动方案，更没有验证我们的双游戏程序集 |
| UE 5.6 回退 | 派生 BuildCookRun，通过部署回调补 Stage；旧菜单改走完整构建 | 当前 UE 5.9 无须移植这组 legacy C++ 菜单及版本宏 |
| CommandUtilities 精确定位命令类型 | 避免为查一条命令而枚举全部类型，连带装入 UHT 不具备的 UAT 依赖 | 新增自动化类时值得借鉴，但仍要验证本地 UHT 装载环境 |
| 其他修改 | 两处 UTF8 属性头文件、旧菜单兼容、嵌套 UAT 的 child-instance/log 隔离 | 不属于本次要提取的移动打包机制，不随手合入 |

作者声明的验证范围是 Windows UE 5.6/5.7/5.8 和 macOS UE 5.8，不能转述成 Android/iOS 已支持。[代码入口](https://github.com/Edenlia/UnrealSharp/blob/f3c0c3680cbe6b4b4850c809cc06a4428362a9fa/Build/Scripts/Staging/UnrealSharpStagingHandler.cs)、[发布与限制](https://github.com/Edenlia/UnrealSharp/blob/f3c0c3680cbe6b4b4850c809cc06a4428362a9fa/Build/Scripts/Utilities/UnrealSharpStaging.cs)。

## 本机 UE 5.9 调用顺序

本地 owning source：`D:/Code/UE5/Engine/Source/Programs/AutomationTool/AutomationUtils/CustomStagingHandler.cs`、`Scripts/BuildCookRun.Automation.cs`、`Scripts/CopyBuildToStagingDirectory.Automation.cs`。

```text
BuildCookRun
  Project.Build                 实际目标的 native/UHT/glue
  Project.Cook                  使用编辑器宿主，不是目标平台宿主
  CopyBuildToStagingDirectory
    从已加载 Automation assemblies 发现 handler，按 SC 初始化
    平台 GetFilesToDeployOrStage
    handler.GetFilesToStage
    StageRuntimeDependenciesFromReceipt
    ModifyDeploymentContextCallback
    目录 remap、PAK/IoStore、文件复制
  Package / Archive / Deploy
```

因此：新增 UFS/NonUFS 文件可以赶上 PAK 与平台签名；移动端 app 托管程序集可以在此发布。Cook 的编辑器共享宿主、native 运行时链接、Android APL/JAR、iOS framework 必须在更早阶段准备，不能全部推迟到 Stage。当前游戏 Driver 是运行时按需创建，若未来资产直接引用某游戏的托管类型，还要单独解决该游戏类型参与 Cook 的要求。

handler 实例会按类型缓存，不可把“已发布”作为一个全局 bool。缓存/去重必须按完整构建键和 generation，不能让第一次 Android 发布盖过随后 Windows 或另一配置。

## 不能直接平移的五处断点

### 1. 当前发布器会嵌套 UAT

`tools/ManagedModules/Program.cs --publish-host` 会运行 PackageProject / PackageProjectMobile。若 handler 在 UAT 内启动这个入口，就重新引入前面遇过的 UAT singleton 冲突。

应把通用 SDK 发布实现提为可在同一 UAT 进程调用的 service；原命令仅作为适配入口。项目模块生产者可以是独立 dotnet 子进程，但其调用链不得再进入 RunUAT。不采用 PR 的跳过 mutex 作为常规编排方案；也继续保留 Cook 不启动第二个 UAT 的本地约束。

### 2. UBT 当前要求先有完整 bundle

本轮修复后的 `Source/SharpDemo/SharpDemo.Build.cs` 会在 native 构建时验证 Bundle.json，并把当前哈希产物登记到 receipt。直接把发布挪到 Stage，会形成“native 要 bundle，而 bundle 又等 native”的循环。

迁移时要同时把 app 清单与文件的登记责任交给 handler。UBT 只负责 native/SDK、目标与能力元数据；Stage 验证完整 bundle，缺失时仍必须失败。不能保留旧 receipt 的哈希文件路径，同时发布新一套再删除旧路径。

### 3. SDK 也在登记托管 app 文件

`DotNetRuntimeSDK.Build.cs::StageManagedContent` 当前按 Content/Managed 平台根的 dll/pdb/json 通配登记，项目规则也登记同一批文件。此外 Android BCL 使用 RuntimeDependencies.Add(dest, source, UFS)；本次实际构建可见 Copy System.* 动作，不能依据“no physical copy”的注释认定没有构建期副本。

Stage 后发布若继续替换这个平台目录，可能删除 native 构建刚生成的 BCL 目的文件，使随后 receipt staging 找不到源文件。SDK 的 app 通配登记也会与 handler 的新输出撞目标路径。

建议把 app 发布源 generation 放在 `Intermediate/UnrealSharp/Publish/<target-key>/...`；SDK 仅拥有 BCL/native/JNI/framework，handler 仅拥有 app/glue/manifest。映射到包内时可保持现有启动路径。SDK 的旧 app 通配登记和项目 managed RuntimeDependencies 要成套退出，不是追加一个 handler 就完成迁移。

### 4. 桌面 NonUFS 策略不能用于全部移动文件

| 文件职责 | 应保持的所有者与发布形式 |
|---|---|
| 桌面 CoreCLR 共享宿主/runtime | handler 映射到包的 Binaries/Managed，NonUFS，供 hostfxr 读取 |
| Android/iOS 托管 app、glue、模块清单 | handler 注册 UFS；运行时继续通过 UE 文件层读取 PAK |
| Mono/CoreCLR BCL | 对应 SDK 的真实目录、身份和 receipt；避免随每游戏重复发布 |
| Android .so、crypto JAR、keep 规则、JNI preload | SDK/UE APL，handler 不接管 |
| iOS Mono/CoreCLR framework、链接与签名 | SDK/平台流水线，handler 不补写已签名 app |
| 游戏选择、存档、视频转换缓存 | Saved 可写数据，不参加发布源 generation |

### 5. 单启动清单不代表所有可选游戏

上游 PackageProject 的默认工程发现不构建显式模块工程。我们需要“共享启动程序集 + 全部随包游戏”的闭包，保留 Profiles/Games 一致性、真实 TFM、框架目录、DLL/PDB 哈希、共享加载映像/API 校验。handler 成功发布宿主，不足以判定整个单包多游戏成功。

## 推荐分层

```text
UBT / DotNetRuntimeSDK
  链接、平台库、BCL、JNI/framework
  receipt: target/config/arch + generic runtime/backend/SDK identity
                         ↓
UnrealSharp.Automation 的通用 Stage handler
  校验 receipt/插件来源 → 创建唯一构建计划 → 持有发布锁
  同进程共享宿主 producer → 项目参与者 producer → 整套校验
  固定不可变 generation → SC.StageFile(明确目的路径) → UE 平台打包
                         ↑
项目 C# 自动化参与者
  读取 Profiles/Games → 两个游戏构建 → 清单与闭包
  不让 UnrealSharp / SDK 认识 PAL3、PAL3A 或未来其他游戏
```

建议的扩展接口概念为 `IManagedPublishParticipant`：接收只读 ManagedPublishContext、临时输出和目标共享引用，返回相对路径、用途（UFS/NonUFS/符号）及验证结果。名字不是既有 API；无需为了研究立即引入。项目实现留在项目自动化程序集，运行时 GameHost 无须引用构建工具。

backend/SDK 选择最好由 native 目标的 receipt 明确提供，UAT 不再手工重算 ini。目标键至少含项目、TargetName/Type、平台、配置、架构、TFM、后端与 SDK 身份；缓存还须校验源/引用输入，不能把构建键当成内容新鲜度证明。

handler 注册后，普通 UE Package、命令行和 CI 共同走 BuildCookRun。现有脚本可以逐步缩为环境检查、Editor Cook 前置和参数入口。只有确认干净输出能从实际平台 UHT/glue 构建后，才能移除当前 host Game-target bootstrap；不能先删前置步骤再等用户遇到缺 glue。

## 实施顺序与验收

1. **只加诊断的 handler**：确认 UE 5.9 从项目/插件自动化程序集发现它，禁用 UnrealSharp 时不触发；记录真实 receipt，暂不发布、不改当前流程。
2. **通用 producer 服务化**：PackageProject 与 Mobile 命令复用同一内部服务；无嵌套 UAT；CommandUtilities 不为执行单条命令装载全部新类型。
3. **项目参与者与完整事务**：调用当前元数据/清单校验；故意令第二游戏编译失败，Stage 终止且旧 generation 完整；锁覆盖发布及文件消费，或持有固定不可变 generation 至复制完。
4. **一次迁移文件所有权**：移除项目 app receipt 登记与 SDK app 通配，保留 native/BCL 所有权；stage 映射及每个目标路径只允许一个源，冲突必须报错。
5. **切默认入口**：Android Mono、Win64 CoreCLR 的干净/增量包先过；之后 Mac、iOS/Simulator、移动 CoreCLR、Shipping。旧路径在对照完成前保留为明确 fallback，不悄悄运行两条发布链。

必须覆盖的触发：标准编辑器 Package、直接 BuildCookRun、`-skipbuild -skipcook -stage`、`-skipstage`、配置/架构变化、错误插件 receipt、缺 SDK、模块失败和中断恢复。Stage-only 应按明确 Build/Verify/Reuse 策略处理，不假装陈旧 glue 已更新。先拒绝 DLC、同目录混目标、universal；不沿用某个 receipt 而悄悄漏掉其他目标。

预期收益是减少人工步骤、保证使用实际目标回执、避免重新计算动态文件清单引起的 UBT makefile 失效、减少 Cook 重编译和输出污染。没有做性能基准，不能承诺会把打包耗时降低多少；native/Cook 本身的成本仍存在。

## 与 DLL 命名的关系

PR 没有实现内容寻址、私有依赖隔离或哈希命名优化。它提供的是“在 Stage 知道完整文件集后显式映射”的入口，使构建缓存路径与包内路径可以分开。哈希是否保留、放文件名还是放目录，是我们下一阶段的独立设计，不能从上游使用普通 DLL 名直接推导出双游戏可全改为普通扁平名。

本轮没有实施该方案；后续命名研究见 `MANAGED_DLL_NAMING_ANALYSIS.md`。
