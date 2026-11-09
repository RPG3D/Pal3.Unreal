# PAL3 诊断与回归

本文对应当前 Win64 开发流程。构建、游戏操作和待办见[项目入口](../README.md)。采集崩溃、托管堆与进程内存时，记录运行模式、关卡、PAL3 地图和场景状态，方便对照同一条件的两次结果。

---

## 1. 现状矩阵

| 故障形态 | UE (C++) 内置能力 | 现状 | C# (Mono) 内置能力 | 本仓库补充 |
|---|---|---|---|---|
| 访问违例 / assert | `CrashReportClient` + `Saved/Crashes/<GUID>/`（minidump + `CrashContext.runtime-xml`，含调用栈、模块） | ✅ 已在用（本次 coreclr AV 就是从 `Saved/Crashes` + Windows 事件日志定位的） | 无 | — |
| GameThread 心跳停止 | `FThreadHeartBeat`，依赖构建配置和 `-debughangdetection` | PIE 使用项目探针 | — | 托管看门狗（`PAL3_WATCHDOG_SECONDS`，默认 20s） |
| 卡在某个脚本 Update | 无（UE 看不到托管帧） | ❌ | 无 | ✅ 单脚本耗时 >100ms 记 `SLOW` |
| 卡在协程 | 无 | ❌ | 无 | ✅ `CoroutineScheduler.Tick` 总耗时 >100ms 记 `SLOW` |
| 托管未捕获异常 | 无 | ❌ | `AppDomain.UnhandledException` 可挂 | ✅ 已挂，写文件 + UE 日志 |
| 后台任务异常被吞 | 无 | ❌ | `TaskScheduler.UnobservedTaskException` | ✅ 已挂 |
| 被吞掉的异常（first-chance） | 无 | ❌ | `FirstChanceException`（需开关） | ✅ `PAL3_TRACE_EXCEPTIONS=1`，1/200 节流 |
| 性能/帧时间 | Unreal Insights、`stat unit` | ✅ 可用 | 无 | 心跳里带帧计数与耗时 |
| 托管与进程内存 | `memreport -full`、`obj list` | Win64 开发验证 | `GC.CollectionCount`、堆用量和累计分配 | `stat UnrealSharp`、Mono 堆快照 |

---

## 2. 已实现：`ManagedDiagnostics`

位置：`Script/Engine/Diagnostics/ManagedDiagnostics.cs`，由 `APal3Driver.BeginPlay` 安装、`Tick` 每帧心跳。

### 开关（环境变量）

| 变量 | 默认 | 作用 |
|---|---|---|
| `PAL3_WATCHDOG_SECONDS` | 20 | 主循环无心跳多少秒判定为卡死 |
| `PAL3_DIAG_DIR` | `./Saved/Logs` | 报告目录 |
| `PAL3_TRACE_EXCEPTIONS` | 关 | `1` = 记录 first-chance 异常（1/200 节流，排查"被吞异常"用） |
| `PAL3_TRACE_UPDATES` | 关 | `1` = 逐脚本打印 `[TickTrace]`，用于缩小发生崩溃的帧内范围 |

### 产物

```
Saved/Logs/managed-diag-YYYYMMDD.log        # 追加写，进程硬崩也保留
[Diag] HANG no frame for 25.0s (phase=..., heartbeat=..., uptime=...)
[Diag] SLOW SomeController.Update took 812ms
[Diag] UNHANDLED System.NullReferenceException ...
```

独立文件可在 UE 日志尚未刷盘时保留诊断信息。看门狗以驱动心跳为准，暂停或结束 PIE 后也可能记录 `HANG`，判断时核对 PIE 状态。

### 已接入的探针

- `GameEntityScriptRuntime.TickAll`：每个脚本 `Update` 计时，>100ms 报 `SLOW`（能直接指到类名）；
- `CoroutineScheduler.Tick`：整体计时，>100ms 报 `SLOW`；
- `APal3Driver.Tick`：每 2s 打印 `actors/scripts/coroutines/meshes/pawn/bounds` 心跳，卡死前后对比最后一行即可知道世界状态。

---

## 3. C++ 侧建议用法（不引入库）

```bash
# 1) 卡死检测（打包版；编辑器里被编译掉）
MyGame.exe -debughangdetection -log

# 2) 崩溃现场
#    Saved/Crashes/<GUID>/CrashContext.runtime-xml  ← ErrorMessage/CallStack/模块
#    Saved/Crashes/<GUID>/UEMinidump.dmp            ← 可用 VS/WinDbg 打开

# 3) 定位是否为托管崩溃
#    Windows 事件查看器：Application 日志 → UnrealEditor.exe 的 APPCRASH，
#    P4=coreclr.dll / P7=c0000005 → 崩溃发生在托管/互操作层
```

补充手段（都在引擎内，无需三方库）：
- `-abslog=<path>`：每次运行独立日志，避免"旧进程占用日志导致读到旧内容"（这次踩过）；
- `Unreal Insights`（`-trace=cpu,frame,gpu`）：卡顿/耗时归因；
- `stat unit` / `stat game`：帧时间分解。

---

## 4. 什么时候才需要第三方库

| 需求 | 建议 |
|---|---|
| 本地开发定位（当前阶段） | **不需要**。UE 内置 + 本模块已覆盖 |
| 远程/多设备自动上报、符号化、聚合看板 | `Crashpad`（Google，跨平台，minidump 上传）或 `Sentry`（`sentry-native` + `sentry-dotnet`，托管/原生通吃，带 release 追踪） |
| 只想要 native 栈符号化 | `dbghelp`/`SymFromAddr`（Windows 内置）+ UE 的 `UnrealSymbolizer` |
| Android 真机崩溃 | 系统 `tombstone`（`adb shell dumpsys dropbox`）+ `logcat -b crash`；UE Android 侧走 `libcrashreporter` |

**建议**：先把"能定位"做扎实（本次已做），等到需要**线上用户**崩溃收集时再上 Sentry/Crashpad——那时它俩的接入成本远低于现在维护一个自研上报链路。

---

## 5. 历史案例 · CoreCLR 互操作崩溃

1. 现象：进程消失、日志截断、无 `LogExit`；
2. 查 `Saved/Crashes/*` 是否有新目录，结合进程日志和系统事件核对；
3. 查 Windows 事件日志 → `P4: coreclr.dll, P7: c0000005` → 托管/互操作层 AV；
4. 对照日志最后一行（`Script [1511] finished running`）+ 当时刚激活的系统（12 个 `Mv3ModelRenderer`）→ 锁定"首次运行角色 Update"这条新路径；
5. 静态审查该路径的 native 调用（`UETransform` 未校验 `IsValid`、`Destroy` 不级联）→ 修复；
6. 仍未复现时：`PAL3_TRACE_UPDATES=1` 逐脚本追踪，最后一行即元凶。

## 6. PAL3 主菜单布局

主菜单布局位于 `Script/ManagedSharpDemo/Layouts/Pal3StartMenu.json`。首页、场景、剧情和存读档是 `menu_pages` 下的四个页面。布局模块将 `type: "switcher"` 创建为 UMG `WidgetSwitcher`，C# 通过 `SetSwitcherPage(switcherId, pageId)` 选择直接子页，并更新按钮文字和可见性。

文本和按钮的 `fontAsset` 可填写字体别名（如 `FZWB`）、包路径（如 `/Game/PAL3/Fonts/Pal3Weibei_Font`）或完整对象路径。省略时使用 `DefaultGame.ini` 中 `[RuntimeWidgetLayout.Fonts]` 的 `Default`。同一配置段的 `FontSizeOffset` 会加到每个 JSON 文本和按钮标签的字号上，当前为 `2`。C# 和蓝图可用 `AddFontMapping` 注册或覆盖别名。蓝图用 `SetDefaultFont`，C# 给 `DefaultFont` 属性赋值。运行时设置影响随后创建的控件。字体资产未安装时回退到 UE 默认字体。

该布局目录通过 `DirectoriesToAlwaysStageAsUFS` 随 UE 打包。C# 向 `CreateJsonWidgetFromFile` 传相对项目根目录的路径，由 UE 文件层读取开发目录或已挂载 Pak。开发时 `PAL3_MENU_LAYOUT` 也可指定绝对路径。

布局作为独立 JSON 文件发布。开发环境修改后，重新创建对应控件即可读取新文件，例如关闭并重开主菜单，或重新进入 PIE。调整位置和样式时保留现有 ID，C# 用这些 ID 绑定按钮和数据。更新 APK 里的布局时，重新 Stage 并打包 UFS 文件。若新增或修改原生 `UFUNCTION`，先重编原生模块并更新绑定。增量构建漏发 `RuntimeWidgetLayout.dll` 时按[构建说明](../README.md#windows-开发启动)单独发布 glue。

```powershell
$env:PAL3_MENU_LAYOUT = 'D:/UEProject/SharpDemo/Script/ManagedSharpDemo/Layouts/Pal3StartMenu.json'
```

## 7. PAL3 剧情与场景自动化

启动编辑器或独立游戏前，给进程设置 `PAL3_AUTOMATION_DIR`。建议每个进程用独立目录。未设置时，运行时不会轮询本地文件。开始 PIE 后，驱动发布会话 ID，客户端向该会话写入带截止时间的请求，游戏线程执行并返回 JSON。客户端超时时会保留最后一次读到的状态，若无响应可继续查托管看门狗日志。

```powershell
# 在启动 UE 的 PowerShell 中设置，然后从同一个环境运行客户端
$env:PAL3_AUTOMATION_DIR = 'D:/UEProject/SharpDemo/Saved/Pal3Automation'
.\tools\pal3-play.ps1 list
.\tools\pal3-play.ps1 status
.\tools\pal3-play.ps1 auto true
.\tools\pal3-play.ps1 start
.\tools\pal3-play.ps1 wait -City q01 -Scene yn09a -ScriptId 1001 -DialogueContains '有贼'
.\tools\pal3-play.ps1 wait -ActorAction z19 -TimeoutSeconds 15

# 在标题或可操作状态下，按 list 返回的 1-based 序号选择
.\tools\pal3-play.ps1 checkpoint 1
.\tools\pal3-play.ps1 scene 2
.\tools\pal3-play.ps1 overlay true
```

- `status` 包括 PAL3 场景、主线变量、队伍、玩家地块和动作、当前对白，以及待运行和运行中的脚本。每条脚本带最近命令、字节偏移和当前 waiter。
- PAL3 尚未启动时，`status` 返回 `bootError` 和 `storagePermissionRequired`。补齐数据或授权后，可用 `retry-boot` 调用与启动界面「重试」共用的入口。
- 菜单切图、剧情跳转、读档和剧情脚本切图使用分帧加载。UE 游戏线程逐批创建纹理、网格、角色和物件，单帧分配约 8 ms，单个批次可能超出预算。加载期间 JSON UMG 显示当前阶段。
- `scene`、`checkpoint` 和 `load` 返回时可能仍在加载。用 `status` 读取 `loadingScene`、`loadingPhase`、`loadingProgress` 和 `sceneLoadError`，或用 `wait -State Gameplay -City <city> -Scene <scene>` 等待可操作场景。进度条按阶段及已处理的批次推进。日志中的 `Scene loaded across` 记录帧数、总耗时和最慢批次。
- `[Lighting]` 记录场景的昼夜标记、环境光、平台 `gain` 和材质实际使用的 `surface` 线性 RGB 增益。整体倍率从 `Config/DefaultEngine.ini` 的 `[Pal3.Lighting]` 读取，键名为平台名加 `SceneBrightness`。当前仅配置 `AndroidSceneBrightness=1.4`，未配置的平台使用 `1.0`。烘焙阴影贴图与景天房间的局部烛光仍分别作用。
- `wait` 可按状态、场景、脚本 ID、最近命令、waiter、玩家动作和对白子串等待。它轮询当前状态，已经结束的短动作需要在发生前开始等待。`advance` 等效于一次手动继续，遇到打字动画可能先补全文字。
- UI 和自动化共用开始、选段及切图路径。UE 日志里的 `[PAL3 transition]` 记录目标、同步分发耗时、失败详情或分发后的状态。后续剧情是否到达预期位置，以 `wait` 或 `status` 为准。
- 诊断面板和对白栏由 C# 管状态，JSON 管布局。游戏中按 Tab 打开主菜单，可开关诊断面板和自动对白。RuntimeWidgetLayout 提供通用的挂载、按键事件、文本、显隐和贴图接口。RC 仍支持 `start`、`advance`、`auto`、`overlay`、`demo`，以及 Actor 查询和控制台命令。
- 改动了 UFUNCTION，需要重编 SharpDemoEditor 原生模块并更新 UnrealSharp 绑定后再运行。对着旧编辑器二进制开启 PIE 会缺少新接口。

### 用 Remote Control 驱动角色

独立游戏启动时加 `-RCWebControlEnable`。客户端指定活动世界里的 `APal3Driver` 对象路径，通过 UE RC 的 `/remote/object/call` 调托管 `ExecutePal3Command`。以下为独立运行的对象路径示例。PIE 使用其活动世界的对象路径。

```powershell
$driver = '/Game/PAL3/Maps/LV_Pal3.LV_Pal3:PersistentLevel.Pal3Driver_C_0'
.\tools\pal3-play.ps1 checkpoint 1 -ObjectPath $driver
.\tools\pal3-play.ps1 wait -State Gameplay -ObjectPath $driver
.\tools\pal3-play.ps1 move 'right 0.75' -ObjectPath $driver
.\tools\pal3-play.ps1 wait -ActorAction c01 -ObjectPath $driver
.\tools\pal3-play.ps1 interact -ObjectPath $driver
.\tools\pal3-play.ps1 status -ObjectPath $driver
```

`move` 将限时的相机相对方向交给 PAL3 移动逻辑，使用导航和动作切换，最多 5 秒。`interact` 走 PAL3 的玩家交互命令，`advance` 直接通知对白和字幕服务。三者均通过游戏命令执行。景天的位置和 `playerHasControl` 可从 `status` 读取。PIE 路径示例为 `/Game/PAL3/Maps/UEDPIE_0_LV_Pal3.LV_Pal3:PersistentLevel.Pal3Driver_C_0`，多实例以当前世界的实际对象路径为准。RC 默认端口为 30010，运行多个进程时分别设置端口并传 `-RemoteUrl`。

`rotate-camera 'right 0.5'` 使用与镜头摇杆共用的输入路径，以满杆向右转半秒。也可传 `left`，最长 5 秒。可加第三个参数测试 `0–1` 的摇杆偏移，例如 `rotate-camera 'right 0.5 0.5'`。`status` 中的 `cameraCanRotate` 和 `cameraYaw` 用来确认机位是否允许旋转以及实际转过的角度。

启用移动端触控布局后，`touch-sticks true` 可将两个摇杆设为全透明，`touch-sticks false` 恢复半透明显示。

`video 1A` 优先播放 UE `Saved/Movies/1a.mp4`，缺少缓存时在运行时从原版数据转码。用 `wait -State VideoPlaying` 确认进入视频流程，用 `advance` 跳过，或等待播放完成后恢复原来的状态。Windows FFmpeg 配置见[项目 README](../README.md)。

### 回归原版剧情选项

```powershell
.\tools\pal3-check-dialogue-choice.ps1 -ObjectPath $driver
```

从标题或可操作状态运行。测试加载渝州预设，用 `move-to '81 357 run'` 从出生点寻路进入场景物件 71 的原版触发区，由场景启动竹筏脚本 287。它检查目的地可通行性、触发位置、提示文字、选项越界拒绝，以及「否」留在 `q01/b`、「是」抵达 `q01/x` 后的角色控制。到达西南后再走进物件 15 的回程触发区，由脚本 288 乘竹筏返回 `q01/b`。

`move-to '<tile-x> <tile-y> [walk|run]'` 通过 RC 交给 PAL3 寻路，要求玩家当前拥有控制权，且目的地在当前层可通行。命令返回代表已提交寻路。可用 `wait -TileX <x> -TileY <y>` 等待指定地块，或用 `wait -ScriptId <id>` 等待途中触发的脚本。

手动调试时，`status` 返回 `choices` 与 `choicePrompt`。`wait -Choices` 等待选项出现，`choose 1` 选择第一项。RC 和 UMG 按钮共用对白服务的选择入口，自动对白会等待玩家或 RC 明确选择。`scene-script` 只用于回放已知原版脚本，要求指定当前 PAL3 城市、场景和脚本 ID。

### 回归开场 MV3 顶点动画

```powershell
.\tools\pal3-play.ps1 pose 0 -ObjectPath $driver
.\tools\pal3-check-opening-animation.ps1 -ObjectPath $driver
```

从标题或可操作状态开始。回归重新开场，在「有贼？！」后分别采样景天 `z19` 和雪见 `c02`。每个动作需在同一次播放内取得至少三种原生顶点指纹。雪见还需有水平位移，局部位移与原生朝向对齐，并在路径上观测到朝向变化。`pose` 提供 `forward` 和本地网格顶点指纹，脚底接地与镜头效果仍需画面对照。

### 存档往返回归

开始界面的「存档与读档」可读取自动存档和手动槽位 1–5，进入可操作状态后可写入手动槽位。界面覆盖旧存档需要再次确认。PAL3 存档默认位于项目的 `Saved/Saves`，仍使用上游的 `slot_N_v1.txt` 命令格式。启动 UE 前可用 `PAL3_SAVE_DIR` 指定独立目录，供测试隔离。

```powershell
.\tools\pal3-play.ps1 slots -ObjectPath $driver
.\tools\pal3-play.ps1 save 5 -ObjectPath $driver
.\tools\pal3-play.ps1 load 5 -ObjectPath $driver
.\tools\pal3-check-save-roundtrip.ps1 -ObjectPath $driver
```

`save` 需要可操作状态，显式指定槽位可覆盖。回归脚本要求空槽，从渝州出生点移到相邻地块后保存，切到另一张 PAL3 地图再恢复，核对场景、地块、世界坐标、主线变量、队伍和控制权。`load` 返回表示读档已提交，`status` 中的 `loadingSave` 和 `saveLoadError` 用于查看完成情况。失败恢复见[当前已知问题](../README.md#当前已知问题与后续项)。

## 8. 内存排查

主仓库固定的 UnrealSharp 子模块已包含内存统计。初始化或更新子模块后重新构建，即可在下表所列的构建中使用相应命令。旧提交 `04044630` 可用下文的 UE 进程、对象和 PAL3 音频指标排查。

| 命令或指标 | 读数 |
|---|---|
| `stat UnrealSharp` | 托管堆估算、分配速率、各代 GC 累计次数、UnrealSharp UObject 句柄和进程内存 |
| `UnrealSharp.DumpStats` | 非 Shipping 构建的单次日志快照，字节字段为 `-1` 表示不可用 |
| `GC Gen 0 collections` | 进程启动以来的 Gen 0 回收次数。要看分配压力，可对比单位时间内的增量 |
| `UnrealSharp.DumpManagedHeap` | Windows Mono 非 Shipping 构建。触发完整 GC 后按类型清点存活对象，输出到 `Saved/UnrealSharp/ManagedHeap-*.csv` |
| `Last Mono census objects` | 最近一次完整堆快照的对象总数，包含普通 C# 对象，执行堆快照命令才刷新 |
| `Tracked UObject handles` | UnrealSharp 对 UObject 包装句柄的登记数量 |
| `[Pal3Driver] ... audio=...` | `decoded` 和 `failed` 为累计计数，`active` 为音频实例数，`pcmBytes` 为这些活跃实例的 PCM 字节数 |

在同一场景状态取基线，重复进出菜单、移动或切图后回到同样状态，再抓完整堆快照和进程私有提交量。CSV 中的 `bytes` 为对象自身占用，用于按类型对比。Windows 可用 `Get-Process -Id <PID>` 读取 `PrivateMemorySize64`，再与 UE 的 `memreport -full`、`obj list class=AudioComponent` 等原生诊断交叉检查。托管堆持平但进程提交量继续增长时，优先查原生对象、缓冲和绑定参数的生命周期。

切换 PAL3 地图时，旧场景销毁后会清空资源缓存。当前界面、角色和音乐播放器仍可持有各自使用中的资源。UE 版按条目读取 CPK，避免整包常驻。用 `scene` 往返两个地图，在同一个地图比较日志 `[Pal3Driver]` 的 `retained` 和 `pendingRelease`。`retained` 是仍由托管侧持有的动态贴图和材质数，`pendingRelease` 是正在等待渲染线程完成的引用数。两者与 `texCreated` 的累计计数口径不同。原生对象最终由 UE GC 回收。

Mono 日志默认使用 `warning`。在开发构建的启动命令行加 `-MonoTraceDebug` 可追踪装载和 GC 细节。

### 历史案例 · 网格数组导致的 Windows 内存耗尽

旧 ProceduralMesh 绑定把 `const TArray&` 参数当作输出参数，生成的 C# 调用没有销毁临时数组。启用逐帧顶点上传后，独立测试进程在约 15 分钟内升到约 172 GiB 私有提交量，最后在申请 101016 字节时失败。项目通用网格入口改用按值数组，生成代码会在调用结束后销毁参数，动画帧仅发送顶点位置。

修复后，在渝州连续采样 40 秒，进程私有提交量从 3659 MiB 到 3651 MiB，期间网格更新计数达到 135985。随后景天 `z19` 与雪见 `c02` 的原生顶点回归通过。测试进程结束后已关闭。

### 历史案例 · 音频组件和 PCM 增长

安宁村 `Q07/Q07` 的旧 PIE 通过 RC 数到 37,222 个 `AudioComponent`。当时完整 GC 后的托管堆约 132 MiB，进程私有提交量已超过 40 GiB。旧循环音频在声道被抢占后会重建组件并重复排入 PCM，脚步声也因找不到已有子音源而叠加。

现行实现由 mixer 的 PCM 生成器在同一个组件内播放和循环，完成后由 UE 回收组件。脚步声复用子音源，过时的播放请求会被跳过。Win64 独立游戏通过 RC 验证了有限播放自然结束、无限循环可停止，以及同场景连续起停 10 次后的回收。此后空跑 80 秒，私有提交量在 3420–3434 MiB 间波动，最后为 20 个音频组件、4 个活跃实例。脚步声听感留待前台复测。
