# SharpDemo · UnrealSharp 跨平台适配与 PAL3 移植

SharpDemo 用于验证 UnrealSharp 在 UE5 上的 **Mono 与移动端 CoreCLR 跨平台适配**。项目覆盖运行时宿主、C# 绑定、托管程序集发布与打包，以及运行时调试和诊断。运行时实现位于 [UnrealSharp 子模块](Plugins/UnrealSharp)。

在这套运行时之上，工程将 PAL3.Unity 移植到 UE5。PAL3 为场景加载、剧情脚本、角色动画、输入、音频和 UI 提供可重复运行的验证场景。移植优先复用上游 C# 玩法，通过 wrapper 对接 UE。[上游代码复用设计](Docs/PAL3_PORTABILITY.md)

## 运行时与平台

| 运行环境 | 当前默认运行时 |
|---|---|
| UE 编辑器，包括 PIE 和由编辑器启动的 `-game` | Mono，由 `DotNetRuntimeEditor=Mono` 指定 |
| Win64 打包游戏 | CoreCLR |
| Android 和 iOS 打包游戏 | Mono |

编辑器运行时和目标平台运行时分别选择。当前默认配置见 [DefaultEngine.ini](Config/DefaultEngine.ini)，移动端 CoreCLR 的适配与组合验证见[运行时记录](Docs/rebuild/DOTNET-SDK-UNIFICATION-PLAN.md)。Windows Android 打包入口是 [BuildAndroid.bat](BuildAndroid.bat)。本轮 PAL3 玩法回归以 Win64 编辑器和独立游戏为基准，运行时验证记录与 PAL3 玩法验收分别维护。

## 工程结构

| 目录 | 职责 |
|---|---|
| `Script/Pal3.Core`、`Script/Pal3.Game` | 原版资源格式、脚本指令和玩法。对照本机 PAL3.Unity 的 `Assets/Scripts` |
| `Script/Engine`、`Script/UnityEngine`、`Script/Pal3.Game/Stubs` | 引擎抽象、Unity 兼容、实体与生命周期桥接 |
| `Script/ManagedSharpDemo` | UE 侧启动和逐帧驱动、PAL3 菜单、HUD 与自动化入口 |
| `Source/RuntimeWidgetLayout`、`Source/SharpDemo` | JSON UMG 和网格、资源、音频等原生接口 |
| `Plugins/UnrealSharp` | 独立 Git 子模块，负责托管运行时、UE 绑定生成和跨语言调用 |

## Windows 开发启动

- 准备与项目关联的 UE 源码构建、UnrealSharp 子模块及其运行时 SDK、可用的 .NET 10 SDK。托管项目目标为 `net10.0`，编辑器使用 `DotNetRuntimeEditor=Mono`。
- 从原版游戏安装目录读取 PAL3 数据。启动 UE 前设置 `PAL3_DATA_PATH`，指向包含游戏资源的安装根目录。
- Android 默认读取内部共享存储的 `Download/PAL3`。保留原版的目录结构和文件名大小写，例如 `Download/PAL3/basedata/basedata.cpk`。首次启动缺权限时，点击「去授权」，在 Android 11 及以上的系统设置中开启「所有文件访问权限」。返回游戏后会重新检查。缺少数据文件时，补齐后点击「重试」。
- 新检出还需要本地托管依赖。在 `Script/Pal3.Core/Libraries` 放入 `Newtonsoft.Json.dll` 和 `INIFileParser.dll`。它们未纳入仓库，可从本地 PAL3.Unity 的 `Assets/Packages` 中取 `Newtonsoft.Json.13.0.3` 和 `ini-parser-netstandard.2.5.2` 的 `lib/netstandard2.0` 版本。

在项目根目录的 PowerShell 运行，路径按本机环境调整。

```powershell
git submodule update --init --recursive
$project = (Resolve-Path .\SharpDemo.uproject).Path
$ue = 'D:\Code\UE5\Engine'
$env:PAL3_DATA_PATH = 'E:\Program Files (x86)\Steam\steamapps\common\PAL3'

& "$ue\Build\BatchFiles\Build.bat" SharpDemoEditor Win64 Development "-Project=$project" -WaitMutex
if ($LASTEXITCODE -ne 0) { throw 'SharpDemoEditor build failed' }

# 从 Script 运行 dotnet，以使用该目录的 global.json。
Push-Location .\Script
try {
    # 若增量生成遗漏了 RuntimeWidgetLayout.dll，或 JSON UMG 接口的绑定过期，先单独发布该 glue。
    # dotnet publish ..\Intermediate\UnrealSharp\UHT\Editor\RuntimeWidgetLayout\RuntimeWidgetLayout.csproj -c Release -p:UETargetType=Editor -o ..\Binaries\Managed\net10.0
    dotnet publish .\ManagedSharpDemo\ManagedSharpDemo.csproj -c Release -p:UETargetType=Editor -o ..\Binaries\Managed\net10.0
    if ($LASTEXITCODE -ne 0) { throw 'Managed publish failed' }
} finally {
    Pop-Location
}

& "$ue\Binaries\Win64\UnrealEditor.exe" $project -game -RCWebControlEnable -log
```

也可用同一编辑器打开 `SharpDemo.uproject`，在默认地图 `LV_Pal3` 开始 PIE。单改托管代码时可从发布步骤开始。改原生 `UFUNCTION` 时，先关闭占用该模块的编辑器或测试进程，再重新构建原生模块并发布托管代码。正常启动会看到 PAL3 标题菜单。

多开编辑器时先发布一次托管程序集，再给用于测试的编辑器实例传入 `-UnrealSharpSkipManagedBuild`。用 Visual Studio F5 启动时，将它追加到调试命令参数中。该实例会加载现有程序集，并跳过启动编译和 C# 热重载。

要在 Visual Studio 同时查看 UE 和 PAL3 的 C++、C# 代码，先生成 UE 解决方案，再从项目根目录运行 `dotnet run --project tools/MergeScriptSln -- SharpDemo.sln`，然后打开 `SharpDemo.mixed.sln`。输入也可以用 `SharpDemo.slnx`，此时输出为 `SharpDemo.mixed.slnx`。选 `SharpDemo` 为启动项目，用 `Development Editor | Win64` 启动。

PAL3 的宿主地图、材质和字体资产放在 `Content/PAL3`，由 `DefaultGame.ini` 的 `DirectoriesToAlwaysCook` 指定 Cook 目录。JSON-UMG 默认字体在同一文件的 `[RuntimeWidgetLayout.Fonts]` 中配置，目前指向方正魏碑。其他机器可将字体文件命名为 `Pal3Weibei.ttf`，在编辑器中导入 `Content/PAL3/Fonts`，并选择同时创建 Font 资产。未导入时使用 UE 默认字体。

启动前设置 `PAL3_AUTOSTART=1` 或传入 `-PAL3Autostart`，可跳过标题菜单，直接进入开场并启用自动对白。独立测试需要隔离存档时，用 `PAL3_SAVE_DIR` 指向测试目录。

## 玩法与调试入口

| 操作 | 入口 |
|---|---|
| 开始、选剧情、切场景、读档 | 标题菜单或可操作状态下的 Tab 菜单。切场景在当前 UE 世界内加载 PAL3 地图 |
| 移动、交互、继续对白 | WASD 或方向键、E、Space 或 Enter。当前交互目标上方会显示提示 |
| 镜头 | Q/R、鼠标滚轮或手柄右摇杆横向转动。固定室内机位和剧情镜头按原作锁定 |
| 移动端操作 | 左下角摇杆移动，右下角「镜头」摇杆横向转动。右下角动作键在自由行动时显示「交互」，在剧情中显示「继续」。右上角 Menu 可开启「摇杆全透明」，触控区仍可操作。Win64 可用 `PAL3_TOUCH_STICK=1` 预览 |
| 返回、诊断、自动对白 | Esc 返回上一级或关闭菜单。Tab 菜单可开关诊断面板和自动对白 |
| 存档 | 可操作且没有运行脚本时可写手动槽 1–5。槽 0 为自动存档，默认目录 `Saved/Saves` |
| 自动化 | `tools/pal3-play.ps1` 通过 RC 或本地文件调用同一套游戏命令。移动使用游戏命令直接驱动 |

主菜单的静态 JSON 定义四个 `WidgetSwitcher` 页面。独立布局文件通过 UFS 随 UE 打包，运行时由 UE 文件接口按项目相对路径读取。C# 负责交互与数据，`RuntimeWidgetLayout` 提供通用的布局、换页和控件 API。诊断面板和对白栏由 `Pal3Hud` 使用 JSON UMG 呈现。旧 `UPal3DebugOverlay` 类仍承载 RC 查询和程序网格接口。[主菜单布局](Script/ManagedSharpDemo/Layouts/Pal3StartMenu.json)

## 当前已知问题与后续项

| 项目 | 当前状态 |
|---|---|
| 读档失败后的恢复 | 已复现。若回放在重置场景后失败，可能停在 `Cutscene`，无法直接重试。此项留待后续修复。调试中遇到时，结束并重开本次 PIE 或独立游戏后再加载有效存档 |
| 增量构建的 UI glue | 旧 solution 缓存可能漏掉 `RuntimeWidgetLayout`，即使加载清单里有它。按上面的命令单独发布对应 glue，缓存问题留待修复 |
| 文件自动化请求 | 无效文件名的 `*.request.json` 可能阻塞后续请求。测试用独立目录，已受影响时可换新会话目录或改用 RC |
| 输入、画面与音频验收 | 已有 RC 回归覆盖场景、剧情、移动、选项和存读档。实体按键、前台 PIE 的视觉效果及脚步声听感仍需人工复测 |
| MonoBehaviour 兼容 | 后续评估。现有玩法由 `APal3Driver` 驱动 `GameEntityScriptRuntime` |
| UE 渲染内存预算 | 待测 Win64 和 Android 上的实际占用，区分普通贴图流送、网格流送、Nanite 和 PAL3 运行时生成的资源。按设备档位评估 `r.Streaming.PoolSize` 等配置，用同地图往返对比显存、加载时间与画质后再定值 |
| PAL3 文件接口 | 后续优先用 UE 文件 API，减少托管代码直接使用 .NET 文件 I/O。评估把 CPK 的外部绝对路径、随机读取和目录枚举接到 UE 平台文件接口，回归 Android `Download/PAL3` 与桌面原版数据路径 |

## 文档导航

- [诊断、RC 与回归](Docs/DIAGNOSTICS.md)记录自动化命令、日志、Mono 堆快照和内存案例。
- [旋转与坐标转换](Docs/QUATERNION_NOTES.md)记录当前 wrapper 的坐标契约和角色转向。
- [画面与角色移植记录](Docs/PAL3_VISUAL_BRINGUP_2026-11-08.md)记录问题定位和历史验证结果。
- [上游代码复用设计](Docs/PAL3_PORTABILITY.md)是分层讨论与后续方案。
- [运行时历史记录](Docs/rebuild/SESSION-STATE.md)和 [UnrealSharp 上游补丁](Docs/upstream/README.md)用于追溯插件和移动端演进。
