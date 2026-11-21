# 固定宿主边界、游戏依赖默认私有（2026-11-22）

本文记录 6565fe7 / 566a830c 检查点。后续收敛已改用严格同批次映像校验，最新策略/版本与验证见 [固定发布收敛记录](MANAGED_FIXED_BUNDLE_2026-11-22.md)；下述 host-closure-v1 为此前阶段。

## 采用的简化

一个进程只选择并运行一款游戏，不实现中途切换、热卸载或多款游戏同时运行。BCL 与必要宿主/UE 绑定共用，其余游戏依赖作为一套私有集合加载。UnityEngine 是否“代码通用”不再触发独立的共享化改造。

| 归属 | 唯一判定来源 |
|---|---|
| 运行时框架 | 所选 SDK 真实生成的 FrameworkAssemblies.json |
| 宿主 | UnrealSharp.Plugins bootstrap、启动 LoadOrder 清单及其实际 DLL 引用闭包，生成 HostAssemblies.json |
| 游戏 | 不属于前两类的游戏发布依赖，默认私有 |

共享底座保留 UnrealSharp、UE glue 和 GameHost.Contracts 等必要接口，以维持跨边界的同一类型身份。宿主工程及其依赖表达边界；目录里有一份 DLL、两个游戏都使用它，均不构成共享依据。宿主应保持独立于游戏，发布器拒绝游戏入口混入宿主图。

Profiles.json 的手写 sharedAssemblies 名单已经删除。两部游戏得到完全一致的宿主契约集合；游戏 Engine、MonoBehaviour 驱动、UnityEngine 和游戏的 JSON/INI 依赖继续私有。相同 DLL 字节仍可在物理存储去重。

## 实现范围

- 项目发布工具新增 HostAssemblyCatalog，读取 PE 引用图，验证完整引用身份，自动生成共享边界和 glue 引用名称。
- 私有编译引用自动指向本次 canonical 宿主；新增宿主依赖无须修改一份逐库名单。
- GameModuleLoader 在执行游戏代码前检查 HostAssemblies.json，拒绝私有程序集覆盖宿主、缺失宿主边界或声明未知共享库。
- 原有程序集身份、真实 TFM、DLL/PDB 哈希、依赖闭包、共享映像证据和完整发布事务全部保留。
- Bundle.json 增加 dependencyPolicy=host-closure-v1，UBT 拒绝旧发布结果或缺宿主清单的包；已有输出须整体重发。
- UnrealSharp 只在 UnrealSharp.Mobile.targets 增加可选的自动宿主引用集合参数；本轮没有改插件 C++、native GC、注册器或程序集加载器。

HostAssemblyCatalog 只检查元数据，不加载被分析的 DLL。共享边界依据宿主依赖声明，不尝试从业务语义猜测哪些库“应当通用”。未来宿主引入依赖时，它会成为宿主拥有的部分，仍需遵守宿主不反向依赖游戏的结构约束。

## 维持的运行模型

启动只加载底座和游戏选择页；确认游戏后注册并加载该游戏的私有集合。修改模块或换游戏需要重启进程。重复 PIE 复用同套程序集并重新创建会话；普通切图、会话销毁和资源清理仍照常执行。

这次收敛主要减少分类和维护决策，没有为了缩短代码删掉一致性检查。未实施 PR #795 的 Stage handler、生产 PathMap、共享 UnityEngine 或新的磁盘命名。

## 验证记录

- .NET 10.0.12 / CoreCLR 11.0.0：各通过七组隔离回归，49 项组内检查。新增 9 项覆盖自动发现宿主的传递依赖、无关 DLL 不提升共享、真实 BCL 归属、私有代码引用宿主而不重复复制、宿主清单篡改、依赖缺失、游戏入口越界及版本不匹配。
- Mono 编辑器：两款游戏均通过完整宿主边界下的列表、中英文切换、选择前不加载游戏、标题和显式 GC；独立测试游戏也能加载其私有依赖，不引入 PAL3 程序集。
- CheckEditorHost.ps1 -CheckOwnershipBoundary 已通过：隔离目录中的坏清单试图替换宿主，被初始化前拒绝，随后同进程成功进入有效游戏标题并完成显式 GC。原始游戏清单未改写。证据为 `Saved/Review/ManagedHost-pal3-20261122-145423/OwnershipRejection.json` 与 Result.json。
- Android 完整重新发布、Cook、Stage、APK 成功。实际 APK 内提取的 HostAssemblies.json 与两款游戏的完整 shared 集合一致；UnityEngine 仍在各自私有集合，22 个 DLL/PDB 全部通过校验、无旧哈希产物。设备未连接，本轮未安装或实机运行。
- Win64 完整打包、归档成功；独立包中两款游戏各通过标题、5 次主动 UE GC、100 个槽查询和全部七个场景快捷入口。结果目录 `Win64Standalone-75adc61d`。
- 主项目、插件及新增文件格式检查通过，测试编辑器与客户端均已退出。复审未发现本轮已覆盖路径中剩余的确认问题；Apple、移动 CoreCLR、Shipping/R8 和新增 Android 包实机仍为未覆盖范围。

证据目录：Saved/Review/HostOwnership-20261122。APK 为 `Binaries/Android/SharpDemo-arm64.apk`，247,107,580 字节，SHA-256 `d08bca1722f19c0eb01ec0db37afe0b88c7028d44434dea1b2264e61cc36ac21`；内容验证见 APK/Result.json、APK/Verify.log。Win64 归档为 ArchivedBuilds/Win64-Development。

本轮改动按用户后续要求 amend 纳入主项目和 UnrealSharp 各自的本地 pal3a 单一功能提交，不推送。前一检查点 f4e7757 / 62daa322 保留在本地备份引用中。
