# 恢复普通装载与热重载（2026-11-23）

Demo 用于验证 UnrealSharp 手机版；双游戏组合由项目适应插件既有能力。本轮验证基于原 Demo `92b6f7a` / 插件 `dac6f5cb` 工作区；修复随本记录并入各自的 pal3a 原提交，不推送。

## 插件范围

以移动端基线 `bae77065` 为参照，移除以下额外机制：

- ManagedAssemblySet、私有 SetLoadContext、manifest入口/schema、精确宿主映像锁定及 AssemblyImageRegistry。
- Plugin/PluginLoader 对集合的路由、路径所有权、per-DLL卸载限制；普通加载恢复按标准文件名工作。
- native RegisteredAssemblyPaths、LogicalName参数、批量注册/预建owner接口及 CSAssemblyLibrary。
- 以集合注册状态决定首次编译的分支；编译器恢复正常 LoadOrder 判定。
- 全局禁止 C# 热重载、队列清理与“须重启”警告状态。
- 私有模块工程标记、UAT/native XML过滤和新增 XmlParser依赖。
- caller-owned prepared发布参数与特殊锁key；默认 PackageProject/PackageProjectMobile 恢复。
- Cook无条件跳过C#构建，以及无关的Mono Fatal后return清理。

保留项都是针对原有工作流程的修复，不认识游戏名：

| 项目 | 原因与验证 |
|---|---|
| 失败加载清理Plugin缓存、名字大小写一致 | 普通DLL的module initializer抛异常后不留下成功缓存；CLI用生产PluginLoader验证。 |
| CSManager独立FGCObject持有引用 | 修复永久/disregard单例不能靠自身UPROPERTY维持晚创建对象的问题。旧Android故障有记录；本轮普通重载与游戏GC通过。没有声称本轮重复了该缺陷的所有负例。 |
| Skeleton延迟解析与无效method handle保护 | 保留引擎/Mono类型初始化时序修复，普通UClass双工程及游戏运行验证。 |
| 必需启动/editor程序集失败终止 | 避免缺失程序集后继续执行未初始化回调；底层加载失败语义恢复原有Fatal。 |
| 同目录多工程监听 | 独立的两个普通csproj共享源码，修复前只更新第一个；按工程登记监听后两个都更新。 |
| Cook的嵌套UAT子实例协议 | 外层UAT启动Cook时继续构建用户C#，复用已编译automation模块，使用UE既有child-instance协议；不新增跳过用户构建的开关。 |
| 无界面初始化失败可靠退出 | 实测Mac的RequestExitWithStatus在此路径忽略退出码，早期Fatal又可能卡在UI恢复；在初始化尚未完成的无人值守路径刷新错误日志并以失败码退出，消除假阳性。 |
| SDK的Android crypto JNI/JAR配套 | 独立runtime移植修复，SDK指针不变。 |

## 项目侧适配

- 共享源码放置不变，分别提供Pal3.* / Pal3A.*普通csproj；每个文件名与AssemblyName一致，分别定义PAL3/PAL3A。
- Unity兼容层也使用Pal3.UnityEngine / Pal3A.UnityEngine，避免不同构建被当作同名DLL覆盖或共享。
- UE反射驱动名区分APal3Driver / APal3ADriver，测试Actor也区分；同名普通CLR类仍由程序集身份区分。
- Directory.Build.props隔离各工程obj，两个工程可在同一目录正常构建；GameHost.Contracts保持纯项目接口，Frontend正常ProjectReference该契约。
- 两套代码加入插件普通启动LoadOrder。GameHost从正常PluginLoader取得已加载入口，菜单只决定启动哪个会话；选择前不加载CPK、不启动剧情。
- 存档、缓存和授权仍分游戏；桌面路径覆盖分别为PAL3_DATA_PATH / PAL3A_DATA_PATH。
- 模块发布工具收缩为现有UAT命令的包装和项目交付校验。编辑器先执行已有BuildUserGlue准备绑定，再BuildUserSolution，解决跨目标后源文件未变但输出被清理的情况。
- 移动端仍由SDK标准规则收录平铺DLL/PDB/LoadOrder，桌面由项目配置正常archive布局的staging映射。

## 独立热重载证据

Script/PluginRegression内两个普通工程编译同一ReloadProbe.cs，各自导出一个不同UE类型，不引用PAL3。

1. 修复前，保存源码后两个函数的结果为 revision-2 / revision-1。
2. 源码显示AddDirectoryToWatch按目录去重，后面的工程未注册回调。
3. 修改为按ProjectName登记，保持上游后续编译、卸载、重载和Blueprint更新流程。
4. 相同用例得到 revision-2 / revision-2；恢复源文件后两个都回到revision-1。

没有新增集合或锁定状态。两次源码保存会按原有调度触发两个工程的重载。旧上下文可能打印上游“did not fully unload”警告；功能更新通过，不把该警告当作已经证明上下文全部释放。

## 验证

- Mac Development原生构建通过；本轮没有修改引擎源码或使用Rider启动。
- 生产PluginLoader的15项普通装载/位置/卸载重载/失败缓存及交付检查通过。CLI只提供UE日志回调，未替换加载器实现。
- PAL3、PAL3A已分别通过标题、第一测试场景、隔离99号槽存读档、主动UE GC；PAL3回归还执行了实际源文件热重载。
- Android、IOS分别用Game UHT生成目标绑定（SkipBuild，不计完整native移动构建），标准PackageProjectMobile成功，14个普通启动根、Collectible=false，输出中无旧Modules目录。
- 编辑器恢复为16个普通启动根、Collectible=true（包含两个editor-only探针）。

Cook验证过程中首先遇到旧Zen项目存储404，改用独立本地CookOutputDir并传SkipZenStore。随后复现嵌套UAT引导失败与“初始化失败却返回0”；修正后RunUAT正常完成，测试前修改的用户C# DLL hash变化，证明实际重新编译，没有跳过C#。完整Cook与故障退出的最终记录见下方追加结果。

### Cook最终结果

- 标准BuildCookRun的Mac地图Cook完整通过，外层退出0，日志无插件初始化失败。预先修改的探针DLL hash变化，确认Cook内确实编译了用户代码。
- 故意向普通探针加入C#编译错误后，UnrealEditor-Cmd退出1，外层UAT退出25，没有COOK COMMAND COMPLETED。测试源码逐字节恢复。
- 用Fatal终止早期错误的中间方案在当前Mac引擎UI恢复中卡住；sample栈记录该情况，已改为无人值守预初始化失败刷新日志后直接失败退出，没有修改引擎。
- 这不等于重新生成APK/IPA；本轮完整Cook目标为Mac，移动端目前验证的是标准托管发布及其交付布局。

最终源码回归：PAL3峰值RSS 3987 MiB，PAL3A 3853 MiB；实际Frontend重载回归 3922 MiB。实际修改共享驱动源码后两套Frontend DLL均变化，PAL3A BeginPlay执行新标记；恢复后两套再次重载。无测试修订标记或编译错误留在源码，全部编辑器/Cook进程已退出。

复审未发现本轮尚未解决的已确认P1/P2；保留的插件改动没有应用宿主schema、游戏名/路径或集合会话合同。

## 边界

此轮没有重新生成完整APK/IPA、覆盖安装或设备回归；移动CoreCLR、Shipping/R8及长时间泄漏检测不计通过。没有启动Android Emulator或iOS Simulator。

相对原移动端基线，插件当前收敛为10文件、+85/-13（其中包含SDK gitlink），SDK内部另有5文件、+52行通用加密修复。当前保留的普通修复与旧集合框架的实现测试分开统计；旧75项检查不再作为已删除框架的当前验收。更早的fixed-bundle/ownership报告保留为历史，当前架构以MANAGED_GAME_MODULES.md和本记录为准。

证据位于Saved/Review/Pal3A-Upstream-Restore-20261123：前后热重载结果、15项CLI检查、两游戏状态/存档结果、两平台标准发布、native构建以及Cook日志。本地Rider引擎GUID、个人配置和用户正式存档保持原样。
