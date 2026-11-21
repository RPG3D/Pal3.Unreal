# PAL3A 插件修复与职责迁移复审（2026-11-23）

本轮修复验证基于原本地 Demo `087ac78` / UnrealSharp `adc02be3`；本记录随修复一起 amend 到各自的 pal3a 原提交，不推送。SDK 指针没有变化。本轮只处理上一份全量审查的 F01 和第一批直接迁移，不调整核心 managed 加载器及模块目录布局。

## F01 修复

- `CSManager::InitialAssemblyLoad` 检查每个必需启动 DLL 的加载结果；失败时在调用方记录含路径和清单名的 Fatal，阻止继续设置初始化完成及广播成功。
- `FUnrealSharpEditorModule` 检查必需的 `UnrealSharp.Editor`；失败时在使用其托管回调之前明确 Fatal。
- 底层 `UCSManagedAssembly::LoadAssembly` 保留 Error/false。动态游戏集合的宿主错误处理不变，没有把可选动态模块失败重新升级为 Fatal。

这里采用的是**必需启动失败明确终止**，没有增加初始化状态机、换上下文机制或失败后继续执行的半初始化模式。

## 迁移清单

| 原插件职责 | 项目落点 | 行为保留与调整 |
|---|---|---|
| CSFileLibrary cpp/header（61+30行） | `Source/SharpDemo/ProjectFileLibrary.cpp`、`.h` | 类改为 `UProjectFileLibrary`、项目导出宏及 Category；函数体不变。仍经 UE UFS 读取文本/字节、解析写路径、同目录原子替换，失败保留旧文件。 |
| 文件 automation（39行） | `Source/SharpDemo/Tests/ProjectFileLibraryTests.cpp` | 测试名改为 `SharpDemo.Files.AtomicReplace`，使用项目头文件相对路径；保留 Windows 锁定目标失败用例。 |
| FrameworkCatalog（29行） | `Script/GameHost/ManagedFrameworkCatalog.cs` | 改为项目 `GameHost` 命名空间；字段、序列化格式和 SDK 扫描逻辑保留。发布器与回归测试链接同一份项目源文件，插件不再编译/导出这个类型。 |
| 共享宿主引用重定向 | 根目录 `Directory.Build.targets` | 私有发布参数改为 `GameHostPublishedSharedAssemblies`；项目 target 在解析引用前更新精确 HintPath，覆盖 Script 和 tools 下的项目。插件 Mobile.targets 恢复到本功能之前的版本，仍提供原有固定 glue 引用规则。 |

GameHost 的三个文件调用方已切换到 `UnrealSharp.SharpDemo.UProjectFileLibrary`；模块发布器、测试引用和本机生成绑定均已同步。

相对插件原移动端基线 `bae77065`，本功能 diff 从 **31 文件、+915/-42** 收敛为 **27 文件、+763/-43**。约 160 行职责已迁出，另补了必需加载调用方的错误检查，所以最终新增行数净减少 152 行。整体项目代码量不会因迁移自动减少。

`ManagedAssemblySet`、`AssemblyImageRegistry`、`ManagedAssemblyMetadata`、`Plugin`、`PluginLoader` 和 `PluginLoadContext` 相对本轮开始均无源码变化；GC、owner 预注册、首次 BP 编译、热重载保护和 SDK 修复仍保留。

## 已完成验证

| 检查 | 结果与证据 |
|---|---|
| Mac UE5.9 Development 原生构建及绑定生成 | 最终构建成功；迁移后的 `ProjectFileLibrary` 与测试已编入 SharpDemo，生成类位于 `UnrealSharp.SharpDemo`。首轮测试 include 路径差异已修正；最终以 `native-rebuild.log` 为准。 |
| 编辑器宿主及两款模块发布 | BuildUserSolution、Editor all 发布、严格 bundle verify 通过。实际重新编译两个变体，覆盖新文件 API 和项目发布 target。 |
| 独立 Probe 发布 | `Editor independent-probe` 在 Saved 下独立输出，实际构建、闭包及宿主映像校验通过，覆盖 tools 根目录项目的真实发布路径。 |
| 托管回归 | .NET 10.0.13，12 组、75 项通过。原 69 项保留，新增 PAL3 Frontend、GameHost、独立 Probe 三个位置的真实 MSBuild target 检查：精确发布引用与普通构建路径各一项。 |
| 必需启动 DLL 损坏 | 使用真实 Mac 原生程序与临时 DLL/load-order fixture，退出码 1，日志和栈停止在 `CSManager::InitialAssemblyLoad` 的明确错误，未继续进入 editor callback 消费。fixture 已删除。 |
| 必需 Editor DLL 损坏 | 临时替换实际 `UnrealSharp.Editor.dll`，退出码 1，停止在必需 editor 加载检查；DLL 按备份逐字节恢复。 |
| 迁移保真与本地设置 | 文件 API/测试与迁移前源码按重命名映射逐字比较一致；核心 managed loader 无 diff。本地 `.uproject`、`.ignore`、`.clang-format`、`.clangd` 的 SHA-256 与操作前一致，Rider GUID 未改。 |
| 静态检查 | 两仓 diff 格式检查、5 个修改的 XML 构建文件解析通过；生产源码不存在旧文件 API 或旧共享引用参数调用。旧审查文档中的历史路径保留。 |

Mac 上 UE 的 Fatal 处理会自行触发 SIGSEGV 并打印 FatalLog 栈。因此两条故障注入测试按**明确的必需加载错误、对应调用栈、非零退出及未进入后续回调**判定；不能仅凭日志出现 SIGSEGV 就误判为原来的空回调问题。测试没有启动模拟器。

### Mac 编辑器运行回归

- PAL3 进入 `q01/yn09a` 标题场景，PAL3A 进入 `q02/qn08y` 标题场景；两者都由宿主菜单实际选择并动态加载，验证了迁移后 JSON、清单和 DLL 字节读取链路。
- 每款游戏完成 3 次主动 UE GC 和短时驻留，标题状态保持正常。项目原生 `SharpDemo.Files.AtomicReplace` automation 成功，覆盖首次写入、替换和完整读取；Windows 专属锁定分支本轮不计通过。
- 同一个隔离 UserDir 连续启动三个进程：首次选择 PAL3，第二次启动读回 PAL3；选择 PAL3A 后，第三次启动读回 PAL3A。用项目原生 ReadText 读取实际选择文件核对，并确认 SavedDirectory 在隔离目录内，没有使用正式存档路径。
- 本轮最终运行采样的峰值 RSS：PAL3 3623.3 MiB、PAL3A 3480.4 MiB、第三次菜单启动 3112.3 MiB；没有触及 12 GiB 保护阈值，未见短时失控增长。不据此排除长时泄漏。
- 测试脚本对每个编辑器使用独立 HTTP 端口，避开快速重启时的端口复用；先确认控制接口可用再调用。只改本地测试启动参数，没有修改产品 RemoteControl 配置。测试编辑器均已退出。

## 复审范围与交付边界

本轮复查必需/动态失败区分、迁移 API 的 native/managed 所有权、生成绑定、框架 DTO 的序列化兼容、项目发布 target 的继承与启用条件，以及普通构建不受发布参数影响的路径。

**复审未发现本轮仍未解决的已确认 P1/P2。** F01 的两条必需启动路径已覆盖故障注入，动态集合失败机制与核心加载器保持原样。第一批项目职责迁移已完成，没有为减行数移除文件安全性和回归用例。

没有顺带进行第二批目录布局迁移、核心加载器政策拆分或通用测试仓库归属调整。Windows 专属锁定失败分支保留，本轮没有重跑 Windows。移动端完整 APK/IPA 和设备回归也没有重新执行；新接口需随新的 native、glue、host 和模块一起重建，不能把本机新托管 DLL 单独配给旧移动端安装包。

原始证据：`Saved/Review/Pal3A-Plugin-Trim-20261123/`，包含原生/宿主/模块构建、75 项回归、故障注入日志和恢复校验、编辑器脚本及内存采样。本地测试目录与用户存档分开。
