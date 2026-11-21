# 固定游戏集合与同批次宿主映像（2026-11-22）

## 目标与边界

只服务目前的产品：启动显示游戏选择页，确认后激活一套游戏程序集，直到进程退出。不建设跨游戏热切换、单 DLL 卸载、激活后更新或不同构建宿主/模块的宽松兼容体系。原有菜单、大地图、99 个手动槽和共享上游源码保留。

BCL 仍由实际 SDK 确定；宿主及 UE glue 的依赖闭包仍自动生成；游戏其余依赖默认私有，UnityEngine/MonoBehaviour 不提升为共享。内容相同文件的磁盘去重与运行时归属分开。

## 本轮收敛

1. 删除 AssemblyContractFingerprint（176 行）和相关编译引用；不再为兼容不同构建不断枚举公开 API 元数据特例。
2. 模块 schema 3 的 shared 记录使用完整 SHA-256，AssemblyImageRegistry 记录实际 Assembly 的身份、MVID、映像 hash。只接受本次发布对应的宿主映像；重建后整体重发并重启。
3. HostAssemblies schema 2 / policy host-images-v2 区分 bootstrap 和项目宿主来源。源码编辑器从插件目录取 bootstrap 真实产物，从项目目录取其余宿主/glue；Editor/References 是按这些来源逐字节复制的编译快照。打包后来源统一为包内共享目录。未重写项目/插件开发输出，不靠同名 DLL 的 API 相同来放行。
4. 普通无 PDB Plugin 恢复原 LoadFromAssemblyPath，保留 Assembly.Location 和相邻文件读取。游戏私有集合继续按 UE/PAK 字节流加载，没有全局伪造 Location。
5. 私有名称激活前检查 Plugin 登记及在途加载；占用时拒绝并要求重启。短期加载 lease 只登记状态，不持有 monitor 执行用户模块初始化。激活后旧路径构造的 Plugin、其他 artifact 路径、错误缓存上下文均不能接管集合名称。
6. 集合中的 per-DLL Unload 请求不拆除集合；直接 Plugin.Unload 明确拒绝。普通 Plugin 继续原生命周期。native 的整套路径注册、首次类型编译和 GC 修复保留，未增加游戏版本 C++ 逻辑。

名称所有权指 UnrealSharp Plugin/native 登记，不能将 AppDomain 中任意同名程序集视为占用。真实编辑器工具会预加载 JSON 库；它们在其他 CLR 上下文里的私有依赖允许与游戏隔离。这个区分是统一规则，没有 Newtonsoft 名称特例。

## 对合并上游的影响

所有权协调实现集中在新增的 ManagedAssemblySet；原 Plugin.cs / PluginLoader.cs 仅保留小的路由、加载 lease、缓存验证和卸载边界调用。普通插件保持原加载语义，插件不认识 PAL3/PAL3A。宿主来源选择和引用快照留在项目 C# 构建工具，未修改 native bootstrap 目录算法或加入 C++ 游戏宏。

相对本轮开始，插件删除的实现行数多于新增；并非撤销已有平台/GC 正确性修复。精确文件匹配不证明 native ABI 或游戏行为正确，实际 SDK/目标配置/依赖校验和平台回归仍需要保留。

## 验证进度

- .NET 10.0.12 / CoreCLR 11.0.0 各通过 10 组隔离回归、65 项组内检查（另有 10 项子进程断言）：严格宿主映像、decimal 常量差异拒绝、普通无 PDB sidecar、已有 Plugin 冲突、在途名称预留、旧 Plugin 延迟加载拒绝、注册路径一致性、同一实例、拒绝卸载、大小写一致的程序集名称所有权和编辑器工具同名依赖隔离均已覆盖。
- Editor/References 的 Binds 文件与真实插件 bootstrap 产物、HostAssemblies 中 SHA-256 三者一致，未把项目同名重编译副本作为 canonical 来源。
- PAL3、PAL3A 均通过 Mono 编辑器选择前不加载游戏、完整同屏列表、中英文切换、动态启动、标题及显式 GC；独立测试游戏也能启动并解析私有依赖。
- PAL3 坏清单覆盖宿主被执行前拒绝后，同进程可以选择正常游戏并启动。最终打包后重新生成 Editor 产物，再次通过此回归；最后记录为 Saved/Review/ManagedHost-pal3-20261122-175705。
- Win64 Development 完整构建、Cook、Stage、Archive 成功。独立包两款游戏各通过 5 次 UE GC、100 槽及全部 7 个快捷场景，结果 Win64Standalone-910f56cf。客户端未出现权限阻挡，测试进程已退出。
- Android Development 完整构建、Cook、Stage、APK 成功。实际 APK 内提取 PAK 后验证 schema 3、policy host-images-v2、共享映像 SHA-256 及 22 个 DLL/PDB，全部一致且无孤立历史产物；crypto Java 类在包内。设备未连接，本轮没有安装或实机启动新包。
- 本轮 review-fix 后未发现已覆盖路径中剩余确认问题。没有改动插件 C++；相对本轮开始插件实现差异为新增 69 行、删除 191 行。源码编辑器、Win64 包、Android 包已覆盖，Apple、移动 CoreCLR、Shipping/R8 和新 Android 包实机仍无本轮运行证据。

## 最终通用性复查与提交

再次按 mobile20261120 基线检查整个插件变更，补获一处普通嵌入式 Plugin 回退语义问题：Default 上下文已有程序集时，候选路径不一定是其真实映像。移除该路径上的 Capture 钩子，PluginLoadContext_Mobile.cs 恢复基线内容；显式集合仍从实际已加载来源验证，严格校验没有放宽。新增四项回归覆盖默认回退、实际程序集、错误宿主拒绝及正确来源接受。

最终 .NET 10.0.12 / CoreCLR 11.0.0 各通过 11 组、69 项组内检查；Mono 编辑器再次验证问情篇、所有权预检拒绝后正常选择、标题与 GC。日志 Saved/Review/FinalGenerality-20261122，最终编辑器记录 ManagedHost-pal3a-20261122-180726。该末轮一行恢复不重出 Android/Win64 包，上文 APK/hash 和独立包结果属于严格映像收敛阶段，不冒充末轮新包实机证据。

复查未发现当前覆盖范围中的剩余阻碍提交问题。相对 mobile20261120，插件为 30 个文件、899 行新增和 42 行删除，新增内容集中于通用元数据、集合加载和文件能力；没有游戏版本 C++ 宏或 PAL3 名称特例。本轮涉及的 .ignore / .vsconfig 独立修改保留在工作区，不纳入功能提交。

证据目录 Saved/Review/FixedBundle-20261122。最新 APK 为 Binaries/Android/SharpDemo-arm64.apk，247,103,696 字节，SHA-256 a138ecbf8cbf4e15e27af962071f01af2ca5ee24732d0645285234b46d8451a9；包内验证为 APK/Result.json、APK/Verify.log。Win64 为 ArchivedBuilds/Win64-Development。

本次源码/目标规则有协议升级，原输出不能与新插件混用；需要完整重新发布。按用户后续要求，最终复查通过后 amend 到父子仓库各自原有的 pal3a 单一功能提交，不推送；原检查点 6565fe7 / 566a830c 保存为本地备份引用。
