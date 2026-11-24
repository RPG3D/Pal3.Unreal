# 普通 UnrealSharp 工程中的双游戏验证

SharpDemo 为 UnrealSharp 移动端移植提供真实负载。游戏组合、命名和会话选择由项目承担；插件沿用普通程序集装载、原生类型注册和 C# 热重载流程。

## 工程组成

同一源码目录有两个普通 csproj：

| 源码目录 | PAL3 | PAL3A |
|---|---|---|
| Script/Pal3.Core | Pal3.Core | Pal3A.Core |
| Script/UnityEngine | Pal3.UnityEngine | Pal3A.UnityEngine |
| Script/Engine | Pal3.Engine | Pal3A.Engine |
| Script/Pal3.Game | Pal3.Game | Pal3A.Game |
| Script/Pal3.Frontend | Pal3.Frontend | Pal3A.Frontend |

每个文件名与 AssemblyName 一致。项目分别声明 PAL3/PAL3A 编译符号和对应项目引用；Directory.Build.props 只隔离每个工程的 obj，防止并行 NuGet/编译冲突。源码与布局资源保持原位。

UE 反射类型也必须区分：驱动分别为 APal3Driver/APal3ADriver，测试 Actor 也分别命名。普通 CLR 类型可以保留同样的命名空间，类型身份包含其程序集。

GameHost.Contracts 定义项目会话接口；GameHost 只从普通 PluginLoader 中取得已经加载的程序集，按目录配置创建所选会话。两套代码参与普通启动 LoadOrder，选择前不启动游戏、不读 CPK。源码不再声明 UnrealSharpModuleProject，也不需要插件扫描过滤。

## 运行和热重载

菜单显示两款游戏。选择记录继续写 Saved/GameHost/SelectedGame.txt，游戏数据授权、存档和缓存仍按游戏分开。桌面覆盖路径分别使用 PAL3_DATA_PATH 与 PAL3A_DATA_PATH，避免一个环境变量把另一款游戏指向错误数据。文件访问由项目的 UProjectFileLibrary 提供。

编辑器使用插件上游的源文件监听、脏工程编译、程序集卸载/重载和 Blueprint 更新。不增加“活动集合”、全局禁止重载或精确宿主映像锁。上游原本在 PIE 期间推迟热重载的行为保留。

修改同目录共享源码时两个工程都需要响应监听。Script/PluginRegression 的两个普通小工程提供不依赖游戏的回归：同一源文件返回 revision-1，修改后两种 UE 类型都应返回 revision-2。修复按工程登记监听，避免只监听同目录中的第一个项目。

## 构建和交付

先用 UE 正常构建生成所需目标的绑定，再调用既有 UAT：

```powershell
# 编辑器：普通 C# 解决方案同时构建两套工程
RunUAT BuildUserSolution -Project=SharpDemo.uproject -OutputPath=Binaries/Managed/net10.0 -TargetConfiguration=Development
RunUAT GenerateUserSolution -Project=SharpDemo.uproject -TargetConfiguration=Development

# 移动端：正常 PackageProjectMobile，输出平铺的 AssemblyName.dll
RunUAT PackageProjectMobile -Project=SharpDemo.uproject -TargetPlatform=Android -TargetArchitecture=arm64 -UETargetType=Game -UEBuildConfig=Development
```

实际调用须带本机引擎路径及插件 ScriptDir。项目工具保留便利包装和交付核验，没有替换插件发布算法：

```powershell
dotnet run --project tools/ManagedModules/ModulePublisher.csproj -- Editor all --engine-root D:/Code/UE5
dotnet run --project tools/ManagedModules/ModulePublisher.csproj -- Android all --publish-host --engine-root D:/Code/UE5
dotnet run --project tools/ManagedModules/ModulePublisher.csproj -- Editor all --verify
```

`--verify` 只检查普通启动布局、程序集身份、入口和依赖顺序，不证明产物的构建配置、架构或后端。该模式拒绝 `--configuration`、`--architecture`、`--target-type`、`--framework`、`--backend` 等构建参数；需要指定构建时执行实际发布。项目没有为此向插件添加新的清单协议。

四个项目打包入口在 Game→Editor 切换后统一调用 `Editor all`，先准备现有绑定再编译用户代码；不能仅依赖原生增量构建重发已清理的 DLL。

发布工具通过标准 `-ScriptsForProject` 将 UAT 构建记录归属到当前项目，与 Cook 子进程使用同一上下文；`-ScriptDir` 仍限定为当前插件的 `Build/Scripts`，避免扫描 Saved 中的历史源码备份。

独立 Cook 与 UAT 托管 Cook 都保留 automation 模块的正常发现和编译。托管上下文按 UAT 自带的继承环境识别，仅跳过运行中 UAT 主程序的自编译，用户 C# 仍照常构建；空缓存与错误退出验证见 [Cook 兼容性修复记录](PAL3A_COOK_COMPATIBILITY_2026-11-25.md)。

移动端通过 SDK 既有规则从 Content/Managed/Android 或 IOS 收录标准 DLL/PDB/LoadOrder。桌面沿用普通 UAT archive 的 Binaries/Managed 布局，由项目 staging 映射；不把 Game 输出复制到 Editor 输出。未使用哈希 DLL 名、模块 schema、HostAssemblies/FrameworkAssemblies 清单、private ALC 或 caller-owned prepared 输出接口。

## 回归

普通加载结构下的完整 APK/IPA 及双游戏真机回归，见 [移动端验证记录（2026-11-25）](PAL3A_ORDINARY_MOBILE_VALIDATION_2026-11-25.md)。

```powershell
dotnet run --project tools/ManagedModules/Tests/ManagedModules.Tests.csproj -c Release
python tools/check-editor-game-host.py --editor PATH_TO_EDITOR --game pal3 --data-path PATH_TO_PAL3 --check-hot-reload
python tools/check-editor-game-host.py --editor PATH_TO_EDITOR --game pal3a --data-path PATH_TO_PAL3A
```

CLI 用生产 PluginLoader 测普通装载、物理位置、卸载/重载、失败缓存及项目包完整性；只有 UE 日志回调由测试提供。编辑器回归使用隔离 UserDir、内存保护、普通共享源码热重载、两款游戏选择/启动、主动 GC 和测试槽存读档。它不启动模拟器。

SDK 的 Android crypto JAR/JNI 修复仍属于运行时移植。移动 CoreCLR、Shipping/R8 等需要各自实机证明，不能由 Mono 通过推定。

2026-11-22 的 fixed-bundle、ownership 和 assembly-set 文档记录历史方案；当前实现以本页及后续普通加载恢复记录为准。
