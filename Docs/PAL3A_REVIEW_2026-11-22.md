# PAL3A 分支审查与后续方案（2026-11-22）

本文保存修复前检查点的审查证据，编号用于跟踪，不代表当前工作区仍未修复。用户后续已授权修复；实施、复审与最新验证见 [修复记录](PAL3A_FIX_AND_REVIEW_2026-11-22.md)。下述“本轮”指当时的只读审查阶段。

本轮只做审查、隔离验证和记录，未执行下述修复，未修改实现代码，未提交或推送。

## 审查范围与结论

| 仓库 | 审查提交 | 对照基线 |
|---|---|---|
| SharpDemo / Pal3.Unreal | `b6bc72c4ce79cd9a4f708688ffd262d4eb32ec21` | `ea1b9a4d4fbda5bb952a274121e03186726511d7` |
| UnrealSharp | `1af7345b4a990b99f24ca4c644df109b9b87420b` | `bae77065b8a1db2f463afd2e02c0afea20d71e9d`，`mobile20261120` |
| DotNetRuntimeSDK | `0fcd948f02b441097624eac04abbfad865419c9e` | `86976ca8596dbc87c2a505cb89ba3a476502d53f` |

方向合理：游戏版本条件保留在 C#；通用 native API、托管程序集装载和游戏会话分开；PAL3 与问情篇复用源码；既有菜单、大地图和存档功能保留。前面的 Android 加密库初始化、动态类型注册顺序和 Disregard for GC 修补，均不作为本轮仍待修的问题重复列出。

但还不能把该检查点视为完整的跨平台发布方案。发现 **1 项 P1、8 项 P2**，主要集中在桌面发布缺链、兼容性检查的遗漏和发布事务边界。现有 PAL3/PAL3A Development Android 冒烟成功，与下列其他路径的问题可以同时成立。

### 问题索引

| 编号 | 优先级 | 问题 | 证据状态 |
|---|---|---|---|
| F01 | P1 | 桌面打包未接入游戏模块发布，默认共享目录也与桌面布局不一致 | 代码调用链确认，未执行完整 Win64/Mac 打包 |
| F02 | P2 | `WriteTextAtomic` 实际先删除旧文件，缺少原子替换保证 | 插件与当前 UE 文件实现交叉确认 |
| F03 | P2 | 共享 API 校验读磁盘文件，实际却可绑定内存中的另一版本 | 隔离探针复现 `MissingMethodException` |
| F04 | P2 | API 指纹遗漏泛型约束和嵌套类型归属 | 两个反例均复现相同指纹 |
| F05 | P2 | TFM 只检查清单自述，发布器也固定写 `net10.0` | net11 标注程序集被 net10 清单放行 |
| F06 | P2 | 以 `System.*` 等前缀代替实际 BCL 目录，造成缺依赖漏检及合法包误拒绝 | 隔离探针复现 `FileNotFoundException` |
| F07 | P2 | 模块发布固定 Development/Game/设备 RID，未贯通构建目标参数 | 命令参数与脚本调用链确认 |
| F08 | P2 | 问情篇仍使用仙剑三的四个自动化场景预设 | 源码与版本条件确认，未在设备触发本条 |
| F09 | P2 | 共享宿主与所有游戏模块不在同一个发布事务中 | 目录替换及多 profile 提交顺序确认 |

## F01 · 桌面发布缺少完整链路

**位置：** [模块发布器](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:16)、[项目 UFS 登记](D:/UEProject/SharpDemo/Source/SharpDemo/SharpDemo.Build.cs:34)、[默认项目发现](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Build/Scripts/Utilities/ProjectUtilities.cs:115)、[桌面 PackageProject](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Build/Scripts/BuildCommands/PackageProject.cs:191)。

**触发与影响：** 从干净输出走现有的桌面 `PackageProject` 流程。该流程通过默认项目发现只发布共享宿主，带 `UnrealSharpModuleProject` 的游戏工程全部被排除。项目只有 Android/iOS 脚本调用 `ModulePublisher`；桌面既没有对应编排，`SharpDemo.Build.cs` 又在 `Modules` 不存在时直接跳过，不会报缺模块。结果可以产生有游戏列表、没有游戏负载的桌面包。

另外，发布器对 `Win64/Mac` 默认查找 `Content/Managed/<Target>/GameHost.Contracts.dll`，桌面共享程序集实际使用 `Binaries/Managed/net10.0`；桌面 `PackageProject` 的目的地还是所选归档下的该目录。当前工作区也没有 `Content/Managed/Win64/Modules` 或 `Content/Managed/Mac/Modules`。显式传第三个目录参数只能处理编译引用，不能自动把模块放入已经归档的包中。

**建议解法：** 在项目侧建立桌面构建编排，明确共享宿主输出、游戏模块输出和 UE Stage/Archive 的顺序。统一传递这些目录，或在通用发布流程中增加项目扩展钩子；插件不认识 PAL3/PAL3A。对配置为随包发布的模块，缺少清单或负载时在 Stage 前失败。

**验收：** 清空独立的 Win64/Mac 测试输出，按文档唯一入口构建；核对包内宿主、两份清单及全部引用文件；脱离源码目录运行，分别选择两部游戏。不得借用之前 Android/Editor 的构建产物。

## F02 · 原子写入 API 不具备原子替换语义

**位置：** [CSFileLibrary.cpp](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Source/UnrealSharpUtilities/Private/CSFileLibrary.cpp:18)。交叉证据：当前引擎 `D:/Code/UE5/Engine/Source/Runtime/Core/Private/HAL/FileManagerGeneric.cpp:299`。

**触发与影响：** 覆盖已有的游戏选择文件时，`Files.Move(..., Replace=true)` 进入 `FFileManagerGeneric::Move`。当前 UE 实现先 `DeleteFile(Dest)`，再调用低层 `MoveFile`。进程若在两步之间退出，旧文件已丢失；移动最终失败时，插件还会删除临时文件。接口名中的 `Atomic` 因此会误导其他调用者，当前可表现为游戏选择丢失并回到默认项。

**建议解法：** 提供经过平台验证的同目录原子替换能力，保留 UE 文件层的路径和沙箱语义，禁止先删除旧目标。平台不能提供该保证时，采用有版本号/校验的双份记录与恢复协议，并明确 API 保证，不能继续宣称原子替换。失败时保留上一个有效文件；临时文件清理应服从恢复协议。

**验收：** 注入重命名失败、目标占用和替换期间进程中断；重新读取必须得到完整旧值或完整新值。这里没有做断电实测，结论来自实际调用的引擎实现。

## F03 · 校验的共享程序集与绑定的共享程序集可能不是同一个

**位置：** [ManagedAssemblySet.cs](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/UnrealSharp/UnrealSharp.Plugins/ManagedAssemblySet.cs:77)、[宿主装载器](D:/UEProject/SharpDemo/Script/GameHost/GameModuleLoader.cs:19)。

**触发与影响：** 宿主已加载旧的共享 DLL，同名磁盘文件更新为新 DLL。校验计算 `readSharedBytes(name)` 的 API 指纹，但 `_shared` 直接沿用调用者提供的已加载 `Assembly`，两者之间没有对应关系校验。模块可通过预检，随后绑定旧代码并缺方法。编辑器未重启时发布共享输出，以及通用 API 的其他调用者，都可能进入该条件；严格固定输出并重启可降低当前游戏路径的触发概率，不能修正 API 本身的缺口。

**复现：** 同名、同版本 `Review.Shared`：v1 只有 `Old()`，v2 增加 `New()`。内存加载 v1，清单及读盘委托提供 v2，入口引用 v2。

```text
REGISTER ACCEPTED with stale in-memory shared assembly
MissingMethodException: Method not found: 'System.String Review.Api.New()'.
```

**建议解法：** 在共享程序集实际初始装载时记录映像身份及指纹，把证据与具体 `Assembly`、加载上下文绑定。模块注册核对该记录；同名程序集存在多个候选时明确拒绝，不从整个 AppDomain 随意选取。只比较 `FullName` 仍不够，同版本 DLL 也可以改变公开 API。

**验收：** 覆盖同版本 API 变更、不同版本、不同公钥身份和多个加载上下文；不兼容必须在执行模块代码前拒绝，并给出重启/重新发布提示。

## F04 · 共享 API 指纹存在可复现的遗漏

**位置：** [AssemblyContractFingerprint.cs](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/Shared/AssemblyContractFingerprint.cs:29)。

**复现与影响：** 以下公开契约变更均得到相同指纹，校验无法发现：

1. `Echo<T>(T value) where T : class` 改为 `where T : struct`。代码没有枚举类型/方法的泛型参数属性和约束。
2. `Review.Left.Item` 移至 `Review.Right.Item`。顶层类型记录使用 `Namespace + Name`，嵌套类型没有把声明类型链计入自己的身份；现有签名解析器虽支持完整嵌套名称，这里没有复用。

```json
{"constraintsHashEqual":true,"nestedOwnerHashEqual":true}
```

**建议解法：** 对公开表面采用完整的声明类型链；计入泛型参数属性和约束，检查有效可见性。对影响二进制绑定的程序集身份、类型转发、调用约定和布局元数据制定明确覆盖规则，再使用稳定排序的规范形式生成指纹。指纹应定位为有边界的契约检查，不能承诺方法行为兼容。

**验收：** 加入上述两个反例；元数据层面的不兼容改变必须改变指纹。方法体或 PDB 路径变动仍应保持稳定，避免重新引入无意义的不匹配。

## F05 · TFM 目前只验证清单字符串

**位置：** [发布器构建及清单](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:41)、[运行时 schema 检查](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/UnrealSharp/UnrealSharp.Plugins/ManagedAssemblySet.cs:65)、[元数据校验](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/Shared/ManagedAssemblyMetadata.cs:36)。

**触发与影响：** profile 指向另一个 TFM 的工程。发布器未指定框架，最后仍固定写 `targetFramework = "net10.0"`；运行时只比较这个字符串与宿主编译 TFM，不检查入口 DLL 的 `TargetFrameworkAttribute`，也不核对框架引用版本。新增独立游戏工程时可出现“清单通过、首次调用才发现框架不兼容”。当前两款游戏工程确实都是 net10，未在现有路径触发本条。

**复现：** 入口 DLL 标注 `.NETCoreApp,Version=v11.0`，清单写 net10，注册和空入口创建均通过。该样例的 IL 故意只使用兼容的基础 API，用来证明预检遗漏；它不表示任意 net11 程序都能在 Mono 10 上运行。

**建议解法：** 从构建计划明确选定入口 TFM，发布后读取实际入口元数据，再生成清单。运行时复核入口 TFM 与框架引用兼容策略。区分入口与第三方依赖，允许策略认可的 netstandard/较低版本依赖，不能把所有 DLL 强制判成 net10。

**验收：** net10 正常；入口实际 net11、清单伪报 net10 必须在执行前拒绝；netstandard 依赖正常。继续保留“net10 程序运行于 CoreCLR 11”的既有正向用例。

## F06 · BCL 前缀分类造成依赖检查漏报与误拒绝

**位置：** [ManagedAssemblyMetadata.cs](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/Shared/ManagedAssemblyMetadata.cs:14)、[发布器排除规则](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:73)、[运行时默认解析](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Managed/UnrealSharp/UnrealSharp.Plugins/ManagedAssemblySet.cs:180)。

**触发与影响：** 合法业务依赖的程序集名称以 `System.*` 或 `Microsoft.Win32.*` 开头，但所选运行时实际不包含它。发布器拒绝将它作为私有依赖携带；若它缺失，闭包校验又把名字前缀视为已由 BCL 满足，直到调用时才失败。

**复现：** 构造 `System.ReviewAddon`，只在清单中提供引用它的入口 DLL。注册通过，执行时报：

```text
REGISTER ACCEPTED with missing System.* dependency
FileNotFoundException: ... 'System.ReviewAddon, Version=0.0.0.0, ...'
```

**建议解法：** 从选定 SDK 的真实框架文件/依赖清单建立名称与身份集合。保护确实由宿主持有的框架程序集；其他同前缀的普通托管包允许显式携带。缺失依赖应在发布及注册时拒绝，不用名字前缀推断文件存在。

**验收：** 真正 BCL 不随模块重复发布；一个独立 `System.*` 包可正确携带；删除它后预检失败。检查私有引用及共享引用的版本/身份是否满足要求，而非只有简单名称相同。

## F07 · 模块与宿主的构建目标参数没有贯通

**位置：** [ModulePublisher.cs:46](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:46)、[RID 选择](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:50)、[BuildAndroid.bat](D:/UEProject/SharpDemo/BuildAndroid.bat:110)。

**触发与影响：** 执行脚本支持的 Shipping/Debug 构建时，共享宿主和 native 目标使用请求的配置，私有模块却固定 `UEBuildConfig=Development`、`-c Release`。编译符号和优化配置因此不一致。当前源码没有据此证明 Shipping 已泄露调试入口，不能把这种后果写成既成事实，但当前参数已明确不匹配。

同一处还把非 Editor 的目标类型固定为 Game，把 IOS RID 固定为 `ios-arm64`，无法准确表达 SDK 已支持的 Client/Server/IOSSimulator 目标。

**建议解法：** 引入项目构建计划，统一传递 Platform、Architecture、TargetType、UEBuildConfig、TFM、RID、共享输出目录，并写入发布描述。PAL3/PAL3A 的差异仍用 C# profile 属性，不增加游戏版本 C++ 宏。

**验收：** 至少检查 Development、Shipping、Debug 的实际 MSBuild 属性及编译符号；设备与模拟器 RID 分别验证。没有 Apple 环境时保留平台待验状态，不能用 Windows 编译代替。

## F08 · 问情篇继承了仙剑三专用的自动化场景快捷表

**位置：** [Pal3StartMenu.cs](D:/UEProject/SharpDemo/Script/Pal3.Frontend/Pal3StartMenu.cs:30)、[自动化 scene 命令](D:/UEProject/SharpDemo/Script/Pal3.Frontend/Pal3Automation.cs:243)。

**触发与影响：** PAL3A 运行时执行 `list` / `scene 1..4`，仍得到“渝州、渝州西南、宾化、镇江”以及 `q01/b`、`q01/xa` 等 PAL3 坐标。表没有变体条件，自动化直接按该表切图，可能找不到资源或落点无效。此前问情篇回归走的是自己的 `checkpoint`，未覆盖这条入口。

本条不等于“整个问情篇场景选择或大地图不可用”：场景菜单主体仍按当前档案查询，大地图也使用当前 `WorldMapConstants`。需要修正的是旧快捷预设及其被借作已知出生点的入口。

**建议解法：** 保留 PAL3 的四个快捷预设，为问情篇提供来自自身场景/剧情数据的预设，或让自动化直接查询场景目录并自动选择可行走落点。预设必须标明所属游戏，并验证地图与坐标；保留自有菜单、大地图和 99 个存档。

**验收：** 两个版本逐一执行所有快捷场景命令，校验城市、地图、层、落点和控制权；不要只验证剧情点。

## F09 · 完整平台发布缺少事务边界

**位置：** [宿主目录替换](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Build/Scripts/BuildCommands/PackageProjectMobile.cs:102)、[宿主备份删除](D:/UEProject/SharpDemo/Plugins/UnrealSharp/Build/Scripts/BuildCommands/PackageProjectMobile.cs:120)、[模块逐 profile 提交](D:/UEProject/SharpDemo/tools/ManagedModules/Program.cs:103)、[脚本步骤](D:/UEProject/SharpDemo/BuildAndroid.bat:110)。

**触发与影响：** 原平台目录已包含一整套可用宿主和 `Modules`。`PackageProjectMobile` 先用只有新宿主的目录替换整个平台目录并删除备份，之后才发布两个游戏；任一模块失败，上一套完整模块已经丢失。单独运行发布器的 `all` 时，则会先提交第一份清单，再构建第二份，失败后形成部分更新状态。

正常 Android 脚本会因错误停止，不能据此宣称它已经把混合版本发给玩家。本条是本地可用输出被破坏、重试/手动 Stage 容易使用不完整组合，以及文档中的原清单保留保证不覆盖完整流程。

**建议解法：** 项目编排在新的 generation 中完成共享宿主和所有必需模块，核对共享身份、入口和文件闭包后，再一次切换平台活动输出。旧 generation 保留到切换成功。通用 SDK 提供暂存输出/后置验证钩子，项目侧负责游戏清单；锁需要覆盖完整事务，不能仅分别锁住两个步骤。

**验收：** 注入宿主成功后失败、第二 profile 编译失败、清单切换前中断；旧活动输出应完整可运行。完整性标记需覆盖宿主和所需模块，不能只看 `UnrealSharp.Plugins.dll` 与 installed flag。

## 结构性不足与后续路线

以下是改进项或边界记录，不计入上面的缺陷数量。

1. **构建描述分散。** `Profiles.json`、`Games.json`、csproj、批处理、Mac 脚本与 manifest schema 各自保存目标或身份信息。以统一构建计划贯通参数，保留“编译配置”和“运行目录”的职责区别，再建立两者一致性检查。优先一起处理 F01、F05、F07、F09。
2. **兼容性验证需分层。** 文件完整性、程序集身份、框架兼容、公开 API 和游戏入口应独立说明保证，避免一个 `apiHash` 承担全部含义。优先补 F03/F04/F06 的负向用例，保留已通过的正向用例。
3. **失败阶段还不明确。** `ManagedAssemblySet._active` 在 native 注册和入口创建之前固定；后续失败会留下需要重启的状态。当前一进程一套模块的限制本身合理，不要求本轮实现卸载。应区分“预检失败可重选”和“已初始化失败须重启”，提前做不执行代码的入口元数据检查，并在 UI 明确提示。
4. **文本命令有两套路径。** 新增 `DebugLogConsole` 仍是反射名称转 SceCommand，而 `CommandText.Parse` 已承担存档/剧情点解析；AddCommand 注册仍为空。后续统一分词、参数验证与错误传播，兼容上游注册委托；同时评估脚本完成结果/run identity，避免仅从“不再调度”推断成功。此处未把未验证的剧情失败结果记为确定缺陷。
5. **可复现的移动端 QA 仍需入库。** 桌面脚本和纯加载器测试已入库；此前装机、偏好恢复和重复 GC 的完整 Android 编排主要在本机 Saved 脚本。应提供版本化的设备命令工具与独立测试数据策略，保留直接游戏命令通道，并把 UE Fatal 与 Android 退出历史联合判断，避免 `EXIT_SELF/0` 被当成正常退出。
6. **平台与资源生命周期边界。** Apple 目录书签、移动 CoreCLR、Simulator、Shipping/R8 仍缺设备证据。SDK crypto JAR 检查、保持 JNI 类与 JVM 初始化方向正确；新 SDK 产物发布时需确认对应后端辅助 JAR 实际进入下载包。旧 SDK 更新要求已写在文档，不重复列作新 bug。

### 推荐实施顺序

1. 补齐桌面交付入口和目标参数贯通，避免继续扩大“只有某个打包脚本可用”的差异。
2. 修复共享映像与 API 校验，再完善 TFM、实际 BCL 集合和负向测试。
3. 修复原子偏好写入与整套发布事务，加入可注入的失败测试。
4. 校正问情篇快捷预设，完成两种游戏的场景/剧情/存档回归。
5. 之后再推进 Apple、移动 CoreCLR、Shipping 和更多独立游戏工程。保持 native 通用 API、C# 引擎适配与游戏逻辑的分界，不引入游戏版本 C++ 宏，不用改动上游玩法代码来掩盖发布层问题。

## 本轮验证与限制

- 当前已有加载器测试：**17 项通过**。这证明既有用例仍正常，未覆盖到上面新增的反例。
- 隔离 Roslyn 样例链接当前构建的 `UnrealSharp.Plugins.dll`，复现了共享内存/文件不一致、两种指纹遗漏、TFM 自述不一致以及缺失 `System.*` 依赖。
- 探针源码和原始输出位于本机 `D:/UEProject/SharpDemo/Saved/Review/Pal3A-20261122/`，不进入发布包。`Harness/Program.cs` 分别以 `fingerprint`、`memory`、`tfm`、`bcl` 运行，各进程隔离单套模块注册状态。
- 本轮没有重新安装 APK、操作设备或重启 UE/Unity，也没有执行完整 Win64/Mac/Apple/Shipping 打包。相应结论已注明为代码调用链或待平台验收。
- 仓库实现及原提交保持不变；本轮新增的版本化内容只有本报告。修复建议须在后续获准实施时执行。
