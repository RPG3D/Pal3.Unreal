# PAL3A UnrealSharp 全量改动审查（2026-11-22）

2026-11-23 后续处置：F01 和第一批直接迁移的实现、验证与复审记录见 `Docs/PAL3A_PLUGIN_TRIM_REVIEW_2026-11-23.md`。下文保留对审查时固定提交的历史判断及逐项索引。

## 结论与推送建议

当前分支已具备 PAL3/PAL3A 的实际使用证据，不属于只有代码、没有交付验证的实现。但本次扩大到**整个插件功能提交**后，确认还有一项启动失败处理的 P2；建议先修正，再推送供他人使用。前一次针对本地追加修改的“可 amend”结论，不能替代本次整个功能提交的审查。

插件的主要问题是职责过多：加载器接入、发布清单政策、文件工具和工程发现规则放进同一提交。约 160 行新增可以优先迁到项目；更大规模减量需要把清单政策与加载机制拆开，不能简单挪走整个加载器。没有证据支持撤销全部 native 修改。

- **自用功能分支**：修复 F01 后，保留现有架构继续真机开发是合理的；迁移项目职责可以分小步完成。
- **共享插件基线或主线候选**：建议先迁出明显项目职责，并让通用加载器回归随插件仓库交付，再推广。当前不能将 PAL3 的正向验证等同于任意 UnrealSharp 工程兼容性验证。
- 本轮没有修改实现、amend 或 push；仅增加本报告及本地审查记录。没有修改引擎、启动编辑器或移动端模拟器。

## 审查边界

| 仓库 | 基线 → 被审提交 | 全量增量 |
|---|---|---|
| SharpDemo | `ea1b9a4d4fbda5bb952a274121e03186726511d7` → `087ac78e24516f7a4a1bd39a0dd470144305160f` | 功能提交 1 个；重点复查插件调用方、项目/插件职责边界，结合已有项目及设备验证 |
| Plugins/UnrealSharp | `bae77065b8a1db2f463afd2e02c0afea20d71e9d` → `adc02be353bda03450501ad0f5c732c02ff773d2` | **31 文件，+915/-42，60 个 diff hunk，全部逐项审查** |
| Source/ThirdParty/DotNetRuntimeSDK | `86976ca8596dbc87c2a505cb89ba3a476502d53f` → `0fcd948f02b441097624eac04abbfad865419c9e` | **5 文件，+52/-0，9 个 hunk，全部逐项审查** |

插件基线是已有移动端分支 `mobile20261120`，不是上游 `main`；本报告不把此前移动端移植的全部历史重新计入 PAL3A 新增代码。SDK 的 52 行独立统计，插件 915 行中只包含它的 gitlink。

按本地远端跟踪引用检查，上述三个新提交尚无包含它们的远端分支；本轮没有 fetch，不能据此描述远端在审查期间的实时变化。本地 Rider 引擎 GUID、`.ignore`、`.clang-format`、`.clangd` 和生成产物没有纳入本次实现变更。

## F01：[P2] 必需程序集加载失败后仍然继续初始化

**变更位置**：插件 `Source/UnrealSharpCore/Private/CSManagedAssembly.cpp:57–61`。

原实现遇到 CLR `LoadPlugin` 返回空 handle 时记录 Fatal；现在重置 `bIsLoading`、记录 Error 并返回 false。这个改动满足“动态游戏模块失败时向宿主报错”的需求，但它修改的是所有程序集共用的底层方法，必需启动路径没有同步接住失败：

1. `CSManager.cpp:224–226` 的 `InitialAssemblyLoad` 忽略 `LoadAssemblyByPath` 返回值。
2. `CSManager.cpp:91–94` 随后仍设置 `bHasInitialized = true` 并广播初始化完成。
3. `UnrealSharpEditor/Private/UnrealSharpEditor.cpp:94–97` 加载 `UnrealSharp.Editor`，同样忽略结果。
4. `UnrealSharpEditor/Public/UnrealSharpEditor.h:50–59` 的托管回调初始为 nullptr；`HotReload/CSHotReloadSubsystem.cpp:39–40` 无条件调用其中的 `LoadSolutionAsync`。

因此，**文件存在但 CLR 加载或模块初始化失败**时，系统可能进入缺失宿主类型或托管回调的半初始化状态；编辑器还可能在后续调用空回调。故障点离实际 DLL 错误更远。缺失文件原本就有返回 false 的路径，此处新增问题特指原先 Fatal 的 CLR 加载失败被全局降级。

这是静态调用链确认的缺陷。本轮没有通过损坏 DLL 启动编辑器做破坏性复现，也不把此前旧 `UnrealSharp.Editor.dll` 的 64/56 字节回调布局不匹配日志当作该空调用路径已经被实测的证明。

**修复方向**：底层保留可失败接口；由必需 bootstrap/load-order/editor 调用方检查结果，中止初始化并报告具体程序集；不要在失败后广播成功。动态游戏模块继续允许返回宿主错误界面。给必需程序集损坏/初始化失败增加回归，不能只把所有动态加载重新改成 Fatal。

**推送判断**：应先修复。这是本次唯一新增、已确认的 P1/P2 级代码问题；以下“迁移”和“补验证”不冒充已发生的运行 bug。

## 插件 31 文件逐项审查

下面路径均相对 `Plugins/UnrealSharp/`；编号与文末逐 hunk 清单对应。“保留”表示职责位置合理，不表示所有平台均已实测。

| 编号 | 文件 / 增删 | 实际变更、影响与审查判断 | 职责建议 |
|---|---|---|---|
| U01 | `Build/Scripts/BuildCommands/PackageProject.cs` +14/-2 | 新增 prepared 输出路径与 FrameworkDependent 参数；非空输出提前拒绝；prepared 分支复用隔离发布 helper 并清理辅助目录。旧入口仍默认 self-contained。项目发布事务消费此接口；桌面包已有交付证据。未发现新增逻辑缺陷。 | **可保留通用输出接口**。也可由项目 UAT 子类接管，但不要为删十几行复制整套发布实现。 |
| U02 | `Build/Scripts/BuildCommands/PackageProjectMobile.cs` +11/-5 | prepared 路径、非空检查；按目的路径区分锁/backup/stamp，避免项目父事务与子发布共用 Mobile.lock；父目录跟随实际目的地。普通入口仍用旧锁名。此处必须与项目事务一起理解。 | 同 U01。项目事务政策已经在项目，插件提供一个目的目录接口即可。 |
| U03 | `Build/Scripts/BuildCommands/PackageProject_Mobile.cs` +2/-2 | helper 新增默认 false 的 selfContained 参数；移动行为不变，桌面 prepared 调用可选择。 | **保留小型通用参数**。helper 是 protected，具备项目子类复用条件；其内部若干发布子步骤仍是 private。 |
| U04 | `Build/Scripts/Utilities/ProjectUtilities.cs` +9/-1 | 扫描项目时读 XML，排除字面值 `UnrealSharpModuleProject=true`，避免宿主自动引用两套游戏。当前 5 个项目使用此标记。它不是完整 MSBuild 属性求值。 | **可随工程目录调整撤回**：把私有模块移出默认 Script 扫描根，发布器保留显式 Profiles 路径。属于中等成本迁移。 |
| U05 | `Managed/Shared/ManagedAssemblyMetadata.cs` 新增173 | PE 元数据读取、完整程序集身份/MVID、TFM/入口、依赖闭包、拓扑排序与环拒绝。发布和运行期共用，避免只信 manifest。细分见下节。 | **拆分政策与机制**：入口/目标框架政策可在项目；已加载映像识别、泛用身份比较可留插件。不能直接删除被加载器引用的公共源。 |
| U06 | `Managed/Shared/ManagedFrameworkCatalog.cs` 新增29 | 从所选 SDK DLL 生成真实 BCL 目录并要求 CoreLib；DTO 供项目读取。插件内部没有调用，仅在 csproj 链接编译；实际消费者是项目发布器、宿主及测试。 | **优先整体迁项目**。加载器已经只接收 `IEnumerable<string>` 身份列表，不需要拥有这个 DTO/扫描器。 |
| U07 | `Managed/UnrealSharp/UnrealSharp.Plugins/AssemblyImageRegistry.cs` 新增35 | 实际 DLL 字节的 identity/MVID/SHA 绑定具体 Assembly；ConditionalWeakTable 不强保活可收集 Assembly；旧内存/新磁盘反例有回归。 | **保留早期映像证据采集**；精确匹配的产品政策可拆到项目。宿主启动后再读磁盘不能替代早期 stream 映像证据。 |
| U08 | `Managed/UnrealSharp/UnrealSharp.Plugins/ManagedAssemblySet.cs` 新增268 | 固定集合清单、预检、名称预留、独立 ALC、私有解析、共享宿主验证、运行期唯一集合和入口实例化。细分见下节。 | **最大拆分候选**：schema/入口/单游戏会话政策迁项目；上下文路由、名称所有权与生命周期约束保留最小插件接口。迁移不是简单移动文件。 |
| U09 | `Managed/UnrealSharp/UnrealSharp.Plugins/Plugin.cs` +9 | 构造时优先使用注册集合 ALC；加载时获取名称 lease；卸载前拒绝活动固定集合。普通路径保留。 | **保留集成钩子**。项目不能可靠地事后替换 Plugin 的私有上下文。 |
| U10 | `Managed/UnrealSharp/UnrealSharp.Plugins/PluginLoadContext.cs` +3/-1 | 实际 DLL 改为 byte[] 读取，加载后记录映像；无 PDB 仍走 LoadFromAssemblyPath，保留 Location/相邻资源语义。普通插件路径/嵌入回退有回归。 | **保留证据能力**。普通 DLL 多一次整文件字节数组，属需测量的 IO/瞬时分配影响，尚无性能回归实测，不列为确认 bug。 |
| U11 | `Managed/UnrealSharp/UnrealSharp.Plugins/PluginLoader.cs` +11/-5 | 字典忽略大小写；hash 物理路径恢复逻辑名；缓存命中校验 owner；失败移除本次新 Plugin；固定集合忽略单 DLL 卸载。末尾仅换行。 | **保留通用接入**。现有 Dictionary 不等于完整并发 API，本改动也未承诺解决所有并行加载；不将旧并发限制误报为新缺陷。 |
| U12 | `Managed/UnrealSharp/UnrealSharp.Plugins/UnrealSharp.Plugins.csproj` +2 | 链接编译两个 Shared 源文件。 | 跟随 U05/U06 迁移；至少可删除 FrameworkCatalog 的编译链接。 |
| U13 | `Source/ThirdParty/DotNetRuntimeSDK` +1/-1 | 子模块推进到 Android crypto JNI 修复；不是单纯略过大子模块 diff，5 文件全审见后文。 | **保留在 SDK 层**。 |
| U14 | `Source/UnrealSharpCompiler/Private/UnrealSharpCompiler.cpp` +4/-2 | 已注册晚加载集合也进入首次 Blueprint 编译；此前只认启动清单中的可热重载程序集。末尾换行独立核对。 | **保留 native 修复**。项目加载了 DLL 不代表原生反射类型已完成首次编译。 |
| U15 | `Source/UnrealSharpCore/Private/CSAssemblyLibrary.cpp` 新增33 | 要求 GameThread；验证 load-order 无重漏；先注册全部 owner，再依赖序加载；返回具体错误。项目检查了该返回值并要求失败后重启。 | **保留薄桥接合理**。可技术性搬进项目 native，但强耦合 Manager，减量收益小。 |
| U16 | `Source/UnrealSharpCore/Private/CSManagedAssembly.cpp` +3/-1 | 失败重置 loading；Fatal 改 Error/false。动态模块分支需要该能力，但必需启动调用方未适配。 | **F01，先修失败传播**；不是直接撤销整个可失败接口。 |
| U17 | `Source/UnrealSharpCore/Private/CSManager.cpp` +78/-7 | 独立 FGCObject 保留晚创建包/owner；全量校验路径与重名冲突后预建 owner，兼容 Mono 依赖初始化；逻辑名与注册路径解析；BeginDestroy 释放 GC bridge。另有 BOM、const、换行整理。 | **保留核心 GC/owner 能力**。项目侧额外 Root 无法替代 Manager 对内部映射的所有权。F01 的调用方也在此文件。 |
| U18 | `Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost_Mono.cpp` -2 | 删除两个 Fatal 后的 return false；Fatal 原本终止，不产生功能收益。 | **建议还原无关清理**，减少与功能无关的 diff；不作为运行缺陷。 |
| U19 | `Source/UnrealSharpCore/Private/Functions/CSFunction.cpp` +11/-1 | Skeleton 临时类延迟 method handle 解析，防 Mono 过早运行初始化；Invoke 在 lazy update 后再次校验 handle，避免 Skeleton 的成功返回被当作已有句柄。参数调用也汇入此路径。 | **保留 native 正确性修复**，不能用项目类改写通用函数编译时序。 |
| U20 | `Source/UnrealSharpCore/Public/CSAssemblyLibrary.h` 新增15 | 上述加载集合的反射 API 声明。 | 跟随 U15。 |
| U21 | `Source/UnrealSharpCore/Public/CSManager.h` +15/-7 | 注册路径/查询/逻辑名 API；FGCObject 生命周期；原 UPROPERTY 引用改由独立 bridge 扫描；BOM/末尾换行。 | **保留并明确需重编译**。原生签名和反射字段变化不应搭配旧 native/旧生成绑定混用。 |
| U22 | `Source/UnrealSharpEditor/Private/HotReload/CSHotReloadSubsystem.cpp` +28/-1 | 活动集合后禁止焦点触发/显式/文件触发热重载；清理旧队列且只警告一次；watcher 不扫描私有模块。guard 已有定向回归。 | **保留禁止无效替换的插件约束**。会话政策可由项目决定；watcher 排除可随 U04 的目录调整撤回。不能只在项目 UI 隐藏重载按钮。 |
| U23 | `Source/UnrealSharpEditor/Public/HotReload/CSHotReloadSubsystem.h` +1 | 单次锁定提示标记。 | 跟随 U22，不值得单独抽象。 |
| U24 | `Source/UnrealSharpUtilities/Private/CSDotnetUtilties.cpp` +7 | Cook commandlet 直接消费提前构建的托管输出，不嵌套启动 UAT；避免同输出目录的重入/锁冲突。 | **可保留通用管线修复**。返回 true 表示跳过构建，不是验证输出合格；项目 publish/verify 必须继续作为前置。推广前还需验证普通工程的 Cook 入口，确保外层确实先发布。 |
| U25 | `Source/UnrealSharpUtilities/Private/CSFileLibrary.cpp` 新增61 | UE UFS 读文本/字节/存在；项目与 Saved 路径；UE 写路径解析后同目录原子替换，失败保留旧文件。 | **优先迁项目**，可接到现有 ProjectStorageLibrary 或独立项目文件库；读 PAK 仍用 UE API，不改成 .NET File 直读。 |
| U26 | `Source/UnrealSharpUtilities/Private/CSProjectUtilities.cpp` +18/-1 | 新 bool 控制过滤，XML 标记识别；默认仍包含全部。跟 U04 保持当前项目格式一致，但不会求值条件/导入属性。 | **随目录调整撤回**；不要为工程文件布局在插件里长期维护两套简化 XML/MSBuild 判断。 |
| U27 | `Source/UnrealSharpUtilities/Private/Tests/CSFileLibraryTests.cpp` 新增39 | 首次写、完整覆盖读取、Windows 锁定目标失败保留旧值；Windows 原生测试已有通过记录。 | **随 U25 迁移，保留测试**。锁定失败分支仅 Windows，不声称覆盖移动平台所有 rename 错误。 |
| U28 | `Source/UnrealSharpUtilities/Public/CSFileLibrary.h` 新增30 | 文件能力的 UFunction 声明。 | 随 U25 迁移，并重生成托管绑定/更新调用命名空间。 |
| U29 | `Source/UnrealSharpUtilities/Public/CSProjectUtilities.h` +1/-1 | GetAllProjectPaths 新增默认 true 的 bool 参数。 | 随 U26；改变 native 签名，不能仅拷贝新 DLL 搭旧 native。 |
| U30 | `Source/UnrealSharpUtilities/UnrealSharpUtilities.Build.cs` +2/-1 | 新增 XmlParser 私有依赖；另有末尾换行。ForceAsEngineGlue 本来就存在，不算本轮新增。 | U04/U26 迁出后删除 XmlParser，避免为工程发现政策增加插件依赖。 |
| U31 | `UnrealSharp.Mobile.targets` +2/-1 | 共享宿主程序集列表增加精确 HintPath 重定向条件；避免重编出的同名 DLL 代替实际 host 快照。 | **可迁项目 publish targets**，明确导入宿主/私有模块及 ProbeGame；保留精确快照语义，不恢复平铺目录盲探测。 |

### 四个新增 managed 文件的内部覆盖

这四个文件合计 **505 行，占插件新增行约 55.2%**。只核对文件数量无法说明它们的设计是否合理，因此单独列出各逻辑段。

| 文件与逻辑段 | 核查结果及边界 |
|---|---|
| Metadata：IsManagedImage / Identity / ModuleId / Name | 使用 PE 元数据，不执行程序集；完整 identity 与 MVID 支撑旧内存/新磁盘识别；public key 与 token 处理保持完整引用语义。基础机制合理。 |
| Metadata：TargetFramework / ValidateTarget | 检查 DLL 真实 TFM，不信清单自报；入口要求目标与 host 一致，依赖允许兼容较低目标。实际高 TFM 被拒绝有回归。具体允许哪些目标属于项目政策。 |
| Metadata：ValidateEntry | 检查入口类型存在、可见性、非抽象/非泛型及无参构造。它只验证此产品的实例化方式，不是 UnrealSharp 所有模块的固有要求；可迁项目。接口实现由 GameHost 实例化后确认。 |
| Metadata：ReferenceIdentity / Satisfies / ValidateClosure | 对完整引用身份检查，包括版本、culture、token；从所选 SDK 名单识别框架，避免把所有 System.* 当 BCL。缺包、旧依赖版本与合法 System.* 私有包均有回归。 |
| Metadata：DependencyOrder | 私有依赖先于消费者，并拒绝环；native owner 仍全部预建，弥补 Mono 可提前执行初始化。环拒绝是当前 loader 合同，不等同 CLR 本身禁止循环引用。 |
| FrameworkCatalog：DTO / FromDirectory | 构建时扫描所选 SDK，排序并要求 CoreLib。runtime loader 只需要身份列表；生成器、平台/backend DTO 的 ownership 可完全归项目。 |
| ImageRegistry：Capture | 捕获真实加载字节的完整身份/MVID/SHA；重复不一致拒绝，弱键不会使 collectible 上下文因证据表而常驻。没有把原始字节长期留在证据表。 |
| ImageRegistry：Verify / fallback | 无记录时绑定实际 Assembly.Location 来源；stream loads 提前 Capture。测试覆盖旧内存与新磁盘、native 实际路径和多上下文歧义。它不是处理不可信恶意 PE 的安全沙箱。 |
| AssemblySet：清单 DTO / constructor | schema 3、编译 host TFM、重复项、真实 DLL 身份/哈希、符号哈希、入口/依赖闭包和拓扑序均预检。HostTargetFramework 是读取插件程序集的目标框架，**不是字面写死 net10，也不是用运行时 major 推断**。 |
| AssemblySet：共享宿主与 BCL | 共享对象使用既有 owner，并按实际映像验证；框架名单与执行 CoreLib 核对，运行期再次核对实际 framework identity。允许不相关 CLR 工具拥有同名私有包，避免把任意 AppDomain 枚举误当 native owner。 |
| AssemblySet：Register / ResolveFile | 在锁内校验后激活；完全相同 JSON 重复注册复用，另一个集合或重建需重启。拒绝绝对路径、盘符、空/点/上级路径段。是发布产物完整性约束，不声称防御管理员替换 symlink 等攻击。 |
| AssemblySet：FindContext / GetLogicalName / cached validation | 按预留名字路由上下文，并校验实际注册路径；hash 物理文件名不会改变程序集逻辑名。缓存不允许拿另一上下文对象顶替。 |
| AssemblySet：BeginPluginLoad / lease | 覆盖执行 module initializer 的期间，避免持有 monitor 跨任意用户初始化；注册不能抢占正在加载的普通插件名；提前构造 Plugin 也不能在选择后用旧 ALC 执行。对应反例有回归。 |
| AssemblySet：RequireUnloadable / CreateEntry | 固定集合禁止单 DLL 卸载，入口按指定类型构造；会话一旦开始执行不能声称支持原子回滚或同进程跨游戏切换。项目已有失败后重启提示。 |
| AssemblySet：SetLoadContext | shared/private/framework 三路受控解析，未声明依赖拒绝；Lazy 避免同名重复加载；载入后移除额外 DLL/PDB byte[]，仅保留映像证据。collectible 状态跟随共享上下文，不代表本产品承诺游戏热卸载。 |

`ManagedAssemblySet` 的“一个选定集合生存到进程结束”约束是当前宿主的设计选择。若推广为通用 UnrealSharp API，应先定义注册/激活/失败后的合同，再将政策移到项目；不能去掉保护，让一次失败之后任意换另一套同名类型继续执行。

## SDK 5 文件逐项审查

下面路径相对 `Plugins/UnrealSharp/Source/ThirdParty/DotNetRuntimeSDK/`。

| 编号 | 文件 / 增删 | 变更、依据与判断 |
|---|---|---|
| S01 | `CopyCoreClrSDKFromSrc.sh` +9 | Android 从同 runtime pack 提取 crypto helper JAR，缺失就终止。配对产物收集应归 SDK；不是 PAL3 专属能力。脚本依赖更新后的 payload，旧产物需要重新生成。 |
| S02 | `CoreClrSDK_APL.xml` +10 | JAR 加入 Gradle libs；JVM 在 coreclr 前 load crypto native，运行 JNI_OnLoad；R8 保留 net.dot.android.crypto 类。静态路径与构建规则一致；本轮无移动 CoreCLR/R8 实机覆盖。 |
| S03 | `DotNetRuntimeSDK.Build.cs` +13 | 共用 RequireAndroidCryptoHelpers，Mono/CoreCLR Android 都检查 JAR，并登记 ExternalDependencies。合理的早失败。存在性检查不证明 JAR/SO 来自同一 build，必须维持 SDK 产物配套。 |
| S04 | `MonoSDK_APL.xml` +11 | 同样 staging JAR、JVM 主动装载 native、R8 keep。不能只靠 P/Invoke dlopen 初始化 JVM 状态；Android Mono 真机以及实际 dex helper 检查有证据。 |
| S05 | `README.md` +9 | 记录 JAR/SO 配套、JNI 初始化与旧 CoreCLR payload 更新要求；与代码一致。 |

这 52 行属于 runtime SDK 的通用修复，**不建议搬到 SharpDemo 的 APL**。搬到项目会让其他使用 SHA/TLS 的插件用户继续踩 JNI 初始化问题，并把 runtime 的装载顺序散落到应用中。完整 TLS、Shipping/R8 与移动 CoreCLR 仍需各自验证。

## 减少插件修改的具体方案

### 第一批：低成本迁出，约 160 行新增

| 改动 | 项目落点与依赖变化 | 为什么可迁 / 需要保留什么 |
|---|---|---|
| CSFileLibrary cpp61 + h30 + tests39，共130行 | 使用项目现有 `Source/SharpDemo/ProjectStorageLibrary.*`，或新增项目文件库；同步迁原生测试与所有 managed 调用，重生成 glue。 | 其职责为 UE UFS 文件访问和选择状态写入，没有用插件私有结构。保留 UE 读 PAK、平台路径解析和同目录原子替换。 |
| ManagedFrameworkCatalog 29行 | 项目共享源码/Contracts，供发布工具链接和 GameHost 反序列化；删除插件 csproj 中对应 Compile 项。 | rg 调用审查显示插件无内部使用；Set 已接受身份列表。不要只迁生成函数、却让 DTO 仍强依赖插件。 |
| PublishedSharedAssemblies 的 targets 条件 | 项目 `Directory.Build.targets` 或发布器显式导入 targets；应覆盖 Script 和 tools 下 ProbeGame 两种根目录。 | 宿主闭包来自项目发布器，此处本来就是项目给出的列表。原有插件固定 glue 引用政策可保留。 |

这组按当前源码粗算约 160 行，相当于插件新增的 **17%左右**；是迁移容量估计，不是已实现、已验证的净 diff。迁入项目会有新的声明/命名空间和引用调整，整体工程代码量不会自动下降。无需为减行数压缩代码或删除回归。

### 第二批：改变项目布局，移除扫描特例

当前 `Engine`、`UnityEngine`、`Pal3.Core`、`Pal3.Game`、`Pal3.Frontend` 五个私有项目仍在插件默认递归扫描的 `Script` 树内，才需要两套 XML 过滤。

可以将私有项目放到该扫描根之外，Profiles.json 继续按显式路径构建，普通 Script 只留 host/contracts/启动程序集。这样可撤销 U04、U26、U29、U30 的 XmlParser 新依赖以及 U22 的 watcher 参数特例，约数十行（约 30 行新增量级）。

迁移要同步修改项目引用、继承 Directory.Build.props 的方式、方案文件、布局资源路径和构建工具。仅把 csproj 改名、放进 Script 的另一层子目录没有用：当前扫描是递归的。此方案比低成本迁出更影响工程布局，不建议与 F01 修复混在一次操作里。

### 第三批：对 505 行核心做职责拆分

项目持有 JSON schema、manifest DTO、入口协议、TFM/宿主精确快照政策、目录解析和选择哪个游戏。插件保留：

1. 明确传入的上下文/注册路径接入普通 Plugin 加载流程。
2. 逻辑名、native owner 和加载中名称的所有权约束。
3. 实际已加载 DLL 的早期映像证据，或一个只读证据查询接口。
4. 被 native 反射引用的对象生命周期、首次编译和有效 method handle 保障。
5. 活动集合不允许被普通单 DLL reload/unload 撕裂的检查。

当前这些职责直接调用 `ManagedAssemblySet` 静态方法。若项目自行建 ALC 却移除所有插件钩子，普通 Plugin 可能继续用全局缓存/旧 ALC，native owner 也可能与执行的 Assembly 脱节。因此不能简单把 268 行拷到 GameHost，再靠反射写 Plugin 的 private 字段或全局 AssemblyResolve 补洞。

这批需要一项明确的加载器接口调整，不在本轮给出未经实现的“还能删掉几百行”承诺。目标应是项目定义政策、插件执行最小机制，而不是建立庞大的可插拔发布框架。

### 适合留在插件的能力

FGCObject 保活、全部 owner 预注册、动态程序集首次 BP 编译、Skeleton handle 时序和 SDK JNI 初始化均有插件或 SDK 层原因。UAT prepared 输出参数也有通用价值：若删除它们迫使项目复制整套私有发布步骤，反而增加维护成本。可以在明确需要项目自有 UAT 入口时复用 protected helper，当前不作为首批减量目标。

## 证据、测试归属和剩余边界

本轮重新运行：

```text
dotnet run --project tools/ManagedModules/Tests/ManagedModules.Tests.csproj -c Release
All loader regression groups passed on 10.0.13
11 组，69 项检查，退出码 0
```

组内检查数为 8/12/4/2/5/11/10/2/4/4/7。覆盖真实 TFM/依赖身份、非法清单、旧内存/新磁盘、实际 native 文件来源、System.* 私有包隔离、发布事务、宿主闭包、普通无 PDB 插件路径、嵌入 Default 回退、重名冲突与激活 lease。Simulator RID 仅是参数单测，没有启动模拟器。

静态检查另通过 SDK 拷贝脚本的 bash 语法检查、两份 APL 与 Mobile.targets 的 XML 解析、插件/SDK 提交的 diff 格式检查。覆盖索引与 git 实际变更文件交叉核对通过：31+5 文件、60+9 个 hunk 均有独立审查记录；这些检查不替代构建和运行验证。

**测试归属仍需改善**：这些加载器回归在 Demo 的 `tools/ManagedModules/Tests`，引用本机已构建的插件 DLL 和宿主 contract；不是在干净插件 checkout 上直接可跑的独立测试。它们也不测试 F01 的 native 初始化链。发布事务/游戏 host 政策测试应留项目；通用加载、映像证据、名称预留及普通插件兼容回归应随插件交付，避免插件后续变更只能依靠 SharpDemo 验证。

本轮没有重复高成本 native/设备回归，引用此前已保存的证据，并区分其范围：

| 已有证据 | 可支持的结论 | 不能据此宣称 |
|---|---|---|
| Mac UE5.9 / Mono 编辑器：两游戏、七快捷场景、主动 UE GC、存读档；完整 native 构建 | 本项目晚加载反射类型、GC 保活及基本玩法路径有实际证据 | 任意工程的普通 hot reload 或所有回调初始化失败安全 |
| Android Mono 与 iPad Mono：授权、存读档、冷启动、跨进程切换；实际 APK/IPA 内容校验 | 两游戏打包与宿主/私有产物边界已落地，不依赖源码目录 DLL | 移动 CoreCLR、Shipping/R8、任意架构和 Client/Server 全部通过 |
| Windows AtomicReplace automation，包含目标占用 | Windows 失败替换保留旧值有原生证据 | 所有移动平台故障模式或断电持久性已覆盖 |
| iPad 最大 footprint约1191 MiB；Android最大PSS约1075 MiB，短时切图/驻留 | 已有采样未见持续失控增长，指标不可跨平台直接比较 | 长时间游玩不存在慢性泄漏 |
| iPad 一次 Trace 初始化崩溃记录 | 发生在 CLR/游戏加载前，已有文档，当前按用户要求不改引擎 | 该启动崩溃由 PAL3A 加载器造成，或已证明 iPad 启动绝对稳定 |

历史验证见 `Docs/PAL3A_MAC_DEVICE_VALIDATION_2026-11-22.md`、`Docs/PAL3A_FIX_AND_REVIEW_2026-11-22.md`；后一份保留了 Windows 阶段当时的 schema 2 等历史说明，**当前插件 schema 为 3**。iPad Trace 记录见 `Docs/IOS_TRACE_STARTUP_CRASH_2026-11-22.md`。

推送前的最小收口顺序建议：

1. 修 F01，验证坏的必需程序集会明确中止初始化；动态游戏加载失败仍能回到宿主错误处理。
2. 第一批职责迁移单独提交，重新生成绑定；检查菜单选择持久化、PAK 读取和项目启动。
3. 将通用加载器回归放回插件可独立运行的位置，补普通插件 unload/hot reload 与无活动集合路径；保留项目 69 项检查。
4. 对实际最终包完成两平台冒烟及包内 host/manifest/hash 一致性检查。改接口后不能用仅旧包测试代替。
5. 确认嵌套 SDK 提交可获取，再推插件，最后推 Demo 子模块指针；Rider GUID 与本地产物继续排除。

原始证据位于 `Saved/Review/Pal3A-Plugin-Audit-20261122/`：插件/SDK 全量 diff、文件与 hunk inventory、本轮 current-tests.log，以及逐项 coverage.json。它们是本地审查产物，不含用户游戏数据，不要求作为发布资源入包。

## 附录：逐 hunk 覆盖索引

以下索引由本次固定提交的 diff 生成；U/S 编号对应上表，每个 hunk 单独记录判断。新增整文件在上表及内部逻辑表逐段核查，不能仅以“一个 hunk”代替内部审查。

| Hunk | Diff 定位 | 判断 |
|---|---|---|
| U01.1 | `-17,6 +17,8` | 通用输出/框架依赖参数说明，可留插件。 |
| U01.2 | `-47,11 +49,21` | prepared空目录与隔离发布分支；项目外层负责事务，可保留接口。 |
| U02.1 | `-15,6 +15,7` | prepared输出参数说明。 |
| U02.2 | `-43,13 +44,18` | 目的目录隔离锁/backup；普通入口仍用Mobile.lock。 |
| U02.3 | `-62,12 +68,12` | 恢复/Stamp跟随实际目的目录与发布key。 |
| U02.4 | `-96,7 +102,7` | 切换前创建实际目的父目录。 |
| U03.1 | `-8,9 +8,9` | helper参数化selfContained，移动默认行为保留。 |
| U04.1 | `-112,7 +112,15` | 私有模块XML排除政策；随项目目录移出扫描根可撤回。 |
| U05.1 | `-0,0 +1,173` | 整文件逐方法核查；PE身份保留，TFM/入口等政策可拆项目。 |
| U06.1 | `-0,0 +1,29` | 整文件核查；实际调用方均为项目，可优先迁出。 |
| U07.1 | `-0,0 +1,35` | 整文件核查；弱键映像证据采集应保留，匹配政策可拆。 |
| U08.1 | `-0,0 +1,268` | 整文件逐逻辑段核查；清单政策可迁，ALC/所有权钩子保留。 |
| U09.1 | `-20,6 +20,13` | 构造优先注册ALC，避免走全局缓存。 |
| U09.2 | `-57,6 +64,7` | 执行期间加载名称lease。 |
| U09.3 | `-81,6 +89,7` | 活动固定集合拒绝单插件卸载。 |
| U10.1 | `-39,7 +39,8` | 读取实际DLL字节供证据采集；无PDB路径语义仍保留。 |
| U10.2 | `-51,6 +52,7` | 加载后捕获实际映像身份，已核对普通插件兼容回归。 |
| U11.1 | `-7,20 +7,24` | 忽略大小写/逻辑名/缓存owner校验。 |
| U11.2 | `-34,6 +38,7` | 失败移除本次新Plugin缓存。 |
| U11.3 | `-53,9 +58,10` | 固定集合卸载保护与逻辑名一致。 |
| U11.4 | `-127,4 +133,4` | 仅末尾换行，无功能变化。 |
| U12.1 | `-13,6 +13,8` | 链接两个Shared源；FrameworkCatalog迁出后删除对应链接。 |
| U13.1 | `-1 +1` | SDK指针；5文件9个hunk另逐项覆盖。 |
| U14.1 | `-202,7 +202,9` | 晚加载注册程序集也执行首次BP编译，应保留。 |
| U14.2 | `-232,4 +234,4` | 仅末尾换行，无功能变化。 |
| U15.1 | `-0,0 +1,33` | 整文件核查；GameThread/顺序完整性/预注册/错误返回，保留薄桥接。 |
| U16.1 | `-56,7 +56,9` | F01：动态失败允许返回；必需启动调用方漏处理，推送前修复。 |
| U17.1 | `-1,4 +1,4` | 仅移除BOM，无功能变化。 |
| U17.2 | `-19,8 +19,70` | FGCObject保活与批量路径校验/预建owner；保留核心机制。 |
| U17.3 | `-32,6 +94,13` | Manager销毁时释放独立GC引用桥接。 |
| U17.4 | `-148,7 +217,7` | 仅循环变量改const，无行为变化。 |
| U17.5 | `-159,11 +228,11` | 显式逻辑名支持hash物理文件，应保留。 |
| U17.6 | `-193,8 +262,10` | 注册路径解析替代固定平铺目录，应保留。 |
| U17.7 | `-316,4 +387,4` | 仅末尾换行，无功能变化。 |
| U18.1 | `-137,13 +137,11` | Fatal后两处return删除；建议还原无关清理。 |
| U19.1 | `-4,6 +4,7` | Skeleton类型include支撑后续判断。 |
| U19.2 | `-27,6 +28,14` | Skeleton延迟解析，避免过早执行Mono初始化。 |
| U19.3 | `-63,7 +72,8` | lazy更新后再次校验有效句柄，与Skeleton跳过逻辑配套。 |
| U20.1 | `-0,0 +1,15` | 整文件核查；加载集合反射API声明，跟随cpp。 |
| U21.1 | `-1,7 +1,8` | GCObject include与BOM清理。 |
| U21.2 | `-20,6 +21,7` | BeginDestroy生命周期声明。 |
| U21.3 | `-36,12 +38,17` | 注册/查询/逻辑名native API；需重生成并重编译。 |
| U21.4 | `-118,20 +125,21` | 独立FGCObject替代UPROPERTY保活，含注册路径表。 |
| U21.5 | `-153,4 +161,4` | 仅末尾换行，无功能变化。 |
| U22.1 | `-71,6 +71,13` | 活动集合后忽略焦点请求。 |
| U22.2 | `-89,6 +96,22` | 显式重载清队列/单次警告；防固定宿主类型被替换。 |
| U22.3 | `-223,7 +246,7` | watcher排除模块；可随目录迁移撤回。 |
| U22.4 | `-318,6 +341,10` | 文件触发路径同样受活动集合保护。 |
| U23.1 | `-101,6 +101,7` | 单次警告状态，与guard配套。 |
| U24.1 | `-396,6 +396,13` | Cook避免嵌套UAT；项目已前置publish/verify，普通Cook仍需独立验证。 |
| U25.1 | `-0,0 +1,61` | 整文件逐API核查；UE读PAK/同目录原子替换，可迁项目。 |
| U26.1 | `-5,6 +5,7` | XmlFile头支撑工程标记解析。 |
| U26.2 | `-83,7 +84,7` | 发现API新增bool，默认保留全部。 |
| U26.3 | `-104,6 +105,22` | XML字面标记过滤，可随目录迁移撤回。 |
| U27.1 | `-0,0 +1,39` | 整文件核查；原子替换回归随文件库迁移，保留Windows失败用例。 |
| U28.1 | `-0,0 +1,30` | 整文件核查；文件UFunction声明随cpp迁项目并重生成绑定。 |
| U29.1 | `-29,6 +29,6` | 发现函数新增默认true参数，与实现配套；迁移后可撤回。 |
| U30.1 | `-19,6 +19,7` | 仅XmlParser是新增依赖，可随过滤撤回。 |
| U30.2 | `-40,4 +41,4` | 仅末尾换行，无功能变化。 |
| U31.1 | `-8,7 +8,8` | 项目共享宿主精确引用重定向，可迁项目publish targets。 |
| S01.1 | `-229,6 +229,15` | Android生产脚本提取配套crypto JAR，缺失早失败，保留SDK。 |
| S02.1 | `-27,6 +27,8` | JAR复制到Gradle libs。 |
| S02.2 | `-39,9 +41,17` | JVM初始化crypto native + R8 keep；CoreCLR实机未覆盖。 |
| S03.1 | `-248,6 +248,16` | 通用JAR存在性与ExternalDependencies检查。 |
| S03.2 | `-257,6 +267,8` | Mono Android调用配套检查。 |
| S03.3 | `-462,6 +474,7` | CoreCLR Android调用配套检查。 |
| S04.1 | `-51,6 +51,8` | Mono JAR复制到Gradle libs。 |
| S04.2 | `-61,9 +63,18` | JVM初始化crypto native + R8 keep；Mono实机有证据。 |
| S05.1 | `-18,6 +18,15` | 配套JAR/SO和JNI初始化文档，与代码一致。 |
