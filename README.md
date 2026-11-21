# SharpDemo = UE5 + UnrealSharp + Pal3.Unity

简体中文 | [English](README_EN.md)

[下载 Android APK（v0.0.2）](https://github.com/RPG3D/Pal3.Unreal/releases/tag/v0.0.2)

SharpDemo 是 **UnrealSharp 移动平台移植项目**的配套工程，用来验证 UnrealSharp 在 Android、iOS 等移动平台上的运行。项目将 Pal3.Unity 移植到 UE5，通过实际游戏流程验证运行时宿主、C# 与 UE 的绑定、托管程序集的发布与打包，以及调试和诊断。工程也用于验证移动端 CoreCLR 适配。 [UnrealSharp 上游仓库](https://github.com/UnrealSharp/UnrealSharp)

[![SharpDemo 在 Android 平板上运行《仙剑奇侠传三》的剧情对白画面，点击查看原图](Docs/images/pal3-android-preview.png)](Docs/images/pal3-android.png)

## 为什么选择 UnrealSharp

- **强类型的 C# 脚本。** 编辑器和编译器能在运行前发现语法错误、类型不匹配，以及接口签名变化引起的调用错误。Lua 和 JavaScript 的动态类型检查通常发生在运行时。
- **成熟的开发和调试工具链。** C# 可用 .NET SDK 构建，在 Visual Studio、Rider 或配有 C# 扩展的 VS Code 中完成代码导航、重构、断点调试和调用栈检查。C++/C# 混合调试的支持情况取决于 IDE、平台和运行时。UE 内嵌的 Lua 和以 QuickJS 运行的 JavaScript 的 IDE 调试取决于对应宿主或插件，断点、单步和变量查看通常需要额外接入和配置。
- **高性能的运行时和跨语言调用。** .NET 的 JIT 可将 IL 编译成原生机器码，Mono 解释器也能在限制动态代码生成的平台执行 IL。UnrealSharp 在构建时静态生成 UE API 的 C# glue，并通过 `delegate* unmanaged` 函数指针直接调用原生绑定入口，减少 C# 与 C++ 之间高频调用的动态查找和分发开销。
- **已有引擎和游戏验证。** Unity、Godot 和 Flax Engine 都支持 C# 脚本。部分商业游戏也有使用 C# 的案例。《黑神话：悟空》PC 版通过 USharp 运行部分 C# 游戏逻辑。《鸣潮》3.6 的客户端脚本层改用 C#。 [CSharpLoader 项目说明](https://github.com/czastack/B1CSharpLoader/blob/master/README.md) · [极客湾实测分析](https://www.bilibili.com/video/av117353606022815/)

## 为什么选择 Pal3.Unity

- **规模适合反复测试。** 一段 RPG 流程就能覆盖场景加载、剧情脚本、角色动画、输入、音频、过场、UI 和存读档。定位问题时，可以直接跳到对应场景或剧情节点。
- **有可运行的 Unity 版本作对照。** 可以用同一套游戏数据运行相同的场景和剧情，对照角色动作、镜头、交互和画面，及时发现 UE 移植引入的差异。 [Pal3.Unity 上游工程](https://github.com/0x7c13/Pal3.Unity)
- **架构优雅，分层清晰，便于跨引擎移植。** `Pal3.Core` 处理资源格式和脚本指令，`Pal3.Game` 实现玩法，`Engine` 抽象实体、变换、贴图等引擎能力。SharpDemo 优先复用上游 C# 玩法，把 Unity 与 UE 的差异集中到引擎适配层，减少后续同步上游代码的成本。 [上游代码复用设计](Docs/PAL3_PORTABILITY.md)

## 运行时与平台

| 运行环境 | 当前默认运行时 |
|---|---|
| UE 编辑器进程，包括 PIE 和 `UnrealEditor -game` | Mono，由 `DotNetRuntimeEditor=Mono` 指定 |
| Win64 打包游戏 | CoreCLR |
| Android 和 iOS 打包游戏 | Mono |

编辑器与打包游戏可分别选择运行时。配置见 [DefaultEngine.ini](Config/DefaultEngine.ini)，移动端 CoreCLR 的适配与验证见[运行时记录](Docs/rebuild/DOTNET-SDK-UNIFICATION-PLAN.md)。在 Windows 上打包 Android APK 使用 [BuildAndroid.bat](BuildAndroid.bat)。

## 工程结构

| 目录 | 职责 |
|---|---|
| `Script/Pal3.Core`、`Script/Pal3.Game` | 原版资源格式、脚本指令和玩法。对照本机 PAL3.Unity 的 `Assets/Scripts` |
| `Script/Engine`、`Script/UnityEngine`、`Script/Pal3.Game/Stubs` | 引擎抽象、Unity 兼容、实体与生命周期桥接 |
| `Script/GameHost.Contracts`、`Script/GameHost`、`Script/ManagedSharpDemo` | 项目 C# 契约、游戏列表与会话选择、UE 启动入口 |
| `Script/Pal3.Frontend` | PAL3 的逐帧驱动、JSON 布局、菜单、HUD 与自动化入口 |
| `Source/RuntimeWidgetLayout`、`Source/SharpDemo` | JSON UMG 和网格、资源、音频等原生接口 |
| `Plugins/UnrealSharp` | 独立 Git 子模块，负责托管运行时、UE 绑定生成和跨语言调用 |

## 准备原版 PAL3 数据

运行游戏需要自备原版 PAL3 数据。保留 `basedata`、`scene`、`music` 等目录及文件名大小写。

- **Windows** 启动前将 `PAL3_DATA_PATH` 设为原版游戏安装根目录。
- **macOS** 调试时优先读取项目的 `Saved/PAL3`，可用符号链接指向 Steam 游戏目录。找不到 `basedata/basedata.cpk` 时，改从 `~/Library/Application Support/Steam/steamapps/common/PAL3` 读取。
- **Android** 直接读取设备共享存储的 `Download/PAL3`，例如 `Download/PAL3/basedata/basedata.cpk`。首次启动时点「去授权」。Android 11 及以上开启「所有文件访问权限」，较早的版本允许读取存储。补齐缺少的数据文件后，点「重试」。
- **iOS** 可将数据放到「我的 iPad/iPhone → SharpDemo → PAL3」。也可在启动界面点「选择 PAL3 文件夹」，选中系统「文件」App 里已下载到本机的 `PAL3` 目录。游戏会保存该目录的访问授权。

Windows 和 Android 在首次播放原版过场时，将视频在运行时转成 MP4，写入 `Saved/Movies` 后播放。iOS 将 `PAL3/movie` 下预先转换的 MP4 缓存到 `Saved/Movies/Imported` 后播放。

## Windows 开发启动

- 准备与项目匹配的 UE 源码构建、UnrealSharp 子模块所需的运行时 SDK，以及 .NET 10 SDK。
- 在 `Script/Pal3.Core/Libraries` 放入 `Newtonsoft.Json.dll` 和 `INIFileParser.dll`。可从本地 Pal3.Unity 的 `Assets/Packages` 中取 `Newtonsoft.Json.13.0.3` 和 `ini-parser-netstandard.2.5.2` 的 `lib/netstandard2.0` 版本。

在项目根目录的 PowerShell 运行，路径按本机环境调整。

```powershell
git submodule update --init --recursive
$project = (Resolve-Path .\SharpDemo.uproject).Path
$projectDir = Split-Path -Parent $project
$ue = 'D:\Code\UE5\Engine'
$env:PAL3_DATA_PATH = 'E:\Program Files (x86)\Steam\steamapps\common\PAL3'
$env:PAL3A_DATA_PATH = 'E:\Program Files (x86)\Steam\steamapps\common\PAL3A'

& "$ue\Build\BatchFiles\Build.bat" SharpDemoEditor Win64 Development "-Project=$project" -WaitMutex
if ($LASTEXITCODE -ne 0) { throw 'SharpDemoEditor build failed' }

$scriptDir = Join-Path $projectDir 'Plugins\UnrealSharp\Build\Scripts'
$managedDir = Join-Path $projectDir 'Binaries\Managed\net10.0'
& "$ue\Build\BatchFiles\RunUAT.bat" BuildUserSolution "-ScriptDir=$scriptDir" "-Project=$project" "-OutputPath=$managedDir" -TargetConfiguration=Development -WaitForUATMutex
if ($LASTEXITCODE -ne 0) { throw 'Managed publish failed' }

dotnet run --project tools/ManagedModules/ModulePublisher.csproj -- Editor all --verify
if ($LASTEXITCODE -ne 0) { throw 'Managed delivery verification failed' }

& "$ue\Binaries\Win64\UnrealEditor.exe" $project -game -RCWebControlEnable -log
```

也可用编辑器打开 `SharpDemo.uproject`，在默认地图 `LV_Pal3` 开始 PIE。`BuildUserSolution` 正常构建并装载两套游戏工程；菜单只选择启动哪个会话。游戏使用不同的程序集名和 UE 类型名，复用源码，不向插件添加集合加载器或热重载锁。C# 热重载沿用 UnrealSharp 原有流程；重复 PIE 会重新创建游戏会话。改原生 `UFUNCTION` 时，先关闭编辑器或测试进程，再重新构建原生模块及生成绑定。详见 [工程组成和验证](Docs/MANAGED_GAME_MODULES.md)。

如果增量构建漏发了 `RuntimeWidgetLayout.dll`，可在 `Script` 目录单独发布对应绑定。

```powershell
dotnet publish ..\Intermediate\UnrealSharp\UHT\Editor\RuntimeWidgetLayout\RuntimeWidgetLayout.csproj -c Release -p:UETargetType=Editor -o ..\Binaries\Managed\net10.0
```

避免多个编辑器同时编译同一份托管输出。日常开发直接使用 UnrealSharp 的 C# 热重载；若需调整自动重载时机，使用插件现有的编辑器设置。

要在 Visual Studio 同时查看 UE 和 PAL3 的 C++、C# 代码，先生成 UE 解决方案，再从项目根目录运行 `dotnet run --project tools/MergeScriptSln -- SharpDemo.sln`，然后打开 `SharpDemo.mixed.sln`。输入也可以用 `SharpDemo.slnx`，此时输出为 `SharpDemo.mixed.slnx`。选 `SharpDemo` 为启动项目，用 `Development Editor | Win64` 启动。

PAL3 的宿主地图、材质和字体资产放在 `Content/PAL3`，由 `DefaultGame.ini` 的 `DirectoriesToAlwaysCook` 指定 Cook 目录。JSON-UMG 默认字体在同一文件的 `[RuntimeWidgetLayout.Fonts]` 中配置，目前指向方正魏碑。其他机器可将字体文件命名为 `Pal3Weibei.ttf`，在编辑器中导入 `Content/PAL3/Fonts`，并选择同时创建 Font 资产。未导入时使用 UE 默认字体。

默认先显示游戏列表，选中并开始后才加载对应游戏 DLL。传入 `-GameHostAutostart` 可跳过游戏列表；配合 `PAL3_AUTOSTART=1` 或 `-PAL3Autostart` 可进一步跳过 PAL3 标题菜单，直接进入开场并启用自动对白。

## 玩法与调试入口

| 操作 | 入口 |
|---|---|
| 开始、选剧情、切场景、读档 | 标题菜单或可操作状态下的 Tab 菜单。切场景在当前 UE 世界内加载 PAL3 地图 |
| 界面语言 | 主菜单点「English」切换英文，按钮随后显示「中文」用于切回。每次启动默认中文 |
| 移动 | WASD 或方向键 |
| 交互和继续对白 | E 交互，Space 或 Enter 继续对白。当前交互目标上方会显示提示 |
| 镜头 | Q/R、鼠标滚轮或手柄右摇杆横向转动。固定室内机位和剧情镜头按原作锁定 |
| 移动端操作 | 左下角摇杆移动，右下角「镜头」摇杆横向转动。右下角动作键在自由行动时显示「交互」，在剧情中显示「继续」。右上角 Menu 可开启「摇杆全透明」，触控区仍可操作。Win64 可用 `PAL3_TOUCH_STICK=1` 预览 |
| 返回、诊断、自动对白 | Esc 返回上一级或关闭菜单。Tab 菜单可开关诊断面板和自动对白 |
| 存档 | 左侧滚动选择槽位。可操作且没有运行脚本时可写手动槽 1–99。槽 0 为自动存档，统一读写 `Saved/Saves` |
| 自动化 | `tools/pal3-play.ps1` 通过 RC 或本地文件调用同一套游戏命令。移动使用游戏命令直接驱动 |

JSON 定义 UMG 布局，C# 负责交互与数据。`RuntimeWidgetLayout` 提供通用的布局、换页和控件 API。主菜单的首页和剧情页通过 `WidgetSwitcher` 切换，场景页按 PAL3 场景档案分组，存读档页按槽位生成。布局文件随 UE 打包为 UFS 文件，运行时通过 UE 文件接口按项目相对路径读取。 [主菜单布局](Script/Pal3.Frontend/Layouts/Pal3StartMenu.json)

界面的中文原文保留在布局和托管文本模板中，英文翻译放在 [Lang-en.json](Script/Pal3.Frontend/Layouts/Lang-en.json)，通过 `textKey` 匹配。文本动态注册到 UE 原生多语言系统，并以 `FText` 显示。
