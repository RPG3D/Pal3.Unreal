# PAL3A 修复与复审记录（2026-11-22）

对应 `PAL3A_REVIEW_2026-11-22.md` 的九项问题。用户要求修复后 review-fix，再研究 PR #795 与 DLL 哈希命名。修复完成后，按用户后续要求 amend 到主项目和插件原有的本地 pal3a 提交，不推送；SDK 本轮无修改。后两项先研究，不直接移植上游代码。

## 修复范围

| 编号 | 实施 | 证据 |
|---|---|---|
| F01 | 增加桌面完整交付入口；共享 Host 在 Stage 映射到包内 Binaries，必需模块缺失提前失败 | Win64 Development 实际 Cook/Stage/Archive 通过；独立包加载两款游戏 |
| F02 | UE 写路径解析后，同目录 Windows MoveFileEx / POSIX rename；失败保留旧文件 | Win64 原生 Automation AtomicReplace 通过，包含目标占用；不声称断电持久性 |
| F03 | 将完整身份、MVID、API 证据绑定具体已加载 Assembly；弱引用表不阻止可收集上下文释放 | 旧内存/新磁盘反例在执行前拒绝，已记录旧 API 仍权威 |
| F04 | API v2：有效可见性、嵌套归属、泛型约束、完整引用身份、布局顺序、转发与互操作元数据 | 泛型/嵌套/私有顺序字段/引用版本改变指纹；方法体和普通私有方法不影响 |
| F05 | 读取实际 DLL 的 TFM 与入口元数据，清单 schema 2 | 伪报 net10、实际 net11 的入口拒绝；全局命名空间入口有效 |
| F06 | 从所选 SDK 生成 FrameworkAssemblies.json；检查依赖完整身份 | 缺 System.* 私有包拒绝，合法包隔离执行；更低依赖版本拒绝 |
| F07 | ManagedBuildPlan 贯通平台、架构、目标、配置、TFM、RID、后端 | 实际 MSBuild 属性已核对 Development/Debug/Shipping；模拟器 RID 单测；UBT 再核对 backend/architecture |
| F08 | PAL3 保留四项；PAL3A 使用本游戏三个上游预设 | Win64 全部七个场景的城市、地图、层、坐标、控制权通过 |
| F09 | 宿主与所有游戏在临时 generation 准备，完整校验后切换整个平台目录 | 生产者失败、切换前/后失败和中断恢复均保留旧完整 bundle；目录占用不会先删除活动输出 |

游戏版本条件继续只在 C#；native API 不认识游戏。保留 JSON UMG、自有菜单、大地图、99 个手动槽以及上游 gameplay 共用路线。本次未调整 DLL 物理命名。

## Review-fix 补充

1. 实际 Win64 打包发现：两参数 RuntimeDependencies.Add 会先把 Game 宿主复制到开发 Binaries，污染 Cook 的 Editor 宿主。已改为仅登记 NonUFS 源文件，DefaultGame.ini 在 Stage 时映射；独立包使用归档内的宿主和 PAK 中模块，不借源码输出。
2. 补上全局命名空间入口、完整签名引用身份、顺序字段声明次序、数组形状和可变参数边界；新增回归覆盖关键反例。
3. 出现过 Windows 目录短暂占用导致 rename 失败；事务保留旧输出，增加有限重试，不删除活动目录来绕过占用。
4. UBT 核对模块与 bundle 的完整目标参数；发布器 verify 也检查入口、SDK 目录、共享宿主与 installed marker，避免只核对 DLL 哈希。
5. 区分元数据预检和初始化阶段：native 注册/会话初始化失败提示重启；预检失败不会占用活动 set。未实现进程内卸载或跨游戏切换。
6. 独立包日志发现资源加载期间每帧查找未注册输入服务。UE 适配侧在 IsGameReady 前返回空输入，不修改上游 ServiceLocator/gameplay。
7. 新增版本化 Android 命令回归工具，备份并恢复选择和存档，验证两个标题、主动 GC、100 槽和所有场景。编辑器回归禁用启动自动构建，防止与打包嵌套 UAT。

8. Mono 编辑器首次回归发现早期 native 装入库与项目同名副本的 MVID 不同。回退证据改读已加载 Assembly.Location 对应的真实来源，仍检查 MVID/API；内存流已在初始加载时记录。新增 native-file 及多上下文歧义用例。两游戏在 Mono 编辑器回归均通过。

## 已完成验证

- 加载器、元数据与事务：.NET 10.0.12 / CoreCLR 11.0.0 各通过六组隔离回归；组内 40 项检查（另有 6 项子进程断言）。
- 真实 MSBuild：Development → Release/WITH_DEVELOPMENT，Debug → Debug/WITH_DEBUG，Shipping → Release/WITH_SHIPPING，均保持 WITH_GAME、PAL3A 和 android-arm64。
- 原生 `UnrealSharp.Files.AtomicReplace`：最终版本再次通过，覆盖首次写入、覆盖、完整读取、锁定失败保留旧值。
- Win64 Development：Cook、Stage、Package、Archive 成功。两游戏在独立用户目录运行，各通过标题、5 次 UE GC、100 存档槽查询；七个快捷场景逐一通过。
- Mono 编辑器：PAL3、PAL3A 均通过 JSON UMG 同屏选择、未开始前不加载游戏 DLL、中英文切换、动态启动与 3 次显式 UE GC。
- Android 最终完整重建成功；从实际 APK 提取 PAK 后，用当前发布器重新核验整套 bundle、两份 schema 2 清单及 22 个 DLL/PDB，文件内容与校验值一致，无孤立哈希文件；crypto Java 辅助类在 dex 中。

原始证据目录：`Saved/Review/Pal3A-20261122/`。主要日志 `Fix-loader-tests-review*.log`、`Fix-MSBuild-configurations.json`、`Fix-AtomicReplace-final.log`、`Fix-Android-final.log`、`Fix-Win64-final.log`、`Fix-Editor-pal3*.log`。最终独立包场景结果为 `Win64Standalone-29df9648/*/Scenes.json`，APK 内验证为 `APK-Final/Result.json` 与 `Verify.log`。

最终 APK：`Binaries/Android/SharpDemo-arm64.apk`，247,105,260 字节，SHA-256 `9a52ad46042c09a007283230e387ce88caa8d5fc39426f4c39ed09630374bfe0`。Win64 归档：`ArchivedBuilds/Win64-Development`。测试用编辑器与客户端进程均已退出；修复按用户后续要求 amend 纳入原有本地提交，不推送。

## 最终复审结论与验证边界

在本次已覆盖的代码和运行路径中，没有剩余已确认 P1/P2；九项修复及 review-fix 均已完成。主项目和插件 diff 格式检查通过。以下是保留的验证边界，不能当作通过：

- Android 设备多次检查均不在 USB/ADB 清单中；本次没有安装或启动新 APK，也没有改设备存档。设备回归脚本已入库待连接后执行，不能将旧 APK 的实机结果归给本次新包。
- Win 客户端测试没有出现授权阻挡；若后续出现，仍按用户要求停止，不尝试绕过。
- Apple、移动 CoreCLR、Simulator、Client/Server 原生目标及 Shipping/R8 设备运行无本轮证据；参数支持和属性检查不等同于交付验证。
- ini 解析当前覆盖项目常用层级，复杂覆盖须显式传 backend/framework-directory；UBT 完整 UE 配置核对不一致时失败。后续 UBT 集成研究将评估收敛这层重复。

编辑器模块事务只覆盖 Content/Managed/Editor，宿主由先行 BuildUserSolution 提供并要求重启；包发布的完整宿主+模块事务使用 --publish-host。目录事务用于构建输出，不支持并发 Stage 消费正在切换的活动目录，也不保证机器断电后的持久性。未启用任意游戏热卸载。

## 后续研究结果

- [PR #795 移动打包方案](UNREALSHARP_PR795_MOBILE_PACKAGING_ANALYSIS.md)：提取 UAT Stage handler / UBT receipt 思路；指出当前前置 bundle 校验、嵌套 UAT、SDK 与项目重复登记等迁移断点，给出通用服务与项目参与者方案。没有复制 PR 或安装 handler。
- [DLL 命名与产物复用](MANAGED_DLL_NAMING_ANALYSIS.md)：同宿主重复发布 hash 稳定；隔离 PathMap 实验验证 wrapper 的重复产物源于生成文件路径。建议先改善构建复用，再考虑短内容目录；未修改生产命名或 PathMap。
