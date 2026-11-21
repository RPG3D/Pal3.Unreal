# PAL3A（问情篇）单安装包方案研究（2026-11-21）

本文保留早期调研和目录变体方案的历史记录。当前架构改为 C# 中立宿主、独立游戏程序集和扁平模块清单，详见 [托管游戏模块](MANAGED_GAME_MODULES.md)。下文 `Variants/`、原生游戏选择接口、`ManagedSubDir` 和裁剪脚本均已被替代，不作为当前操作步骤。“未提交”等状态均为当时记录；当前实现按用户要求保存为本地 `pal3a` 提交，不推送。

目标：一个安装包（APK/Win64）同时包含 PAL3（仙剑三）与 PAL3A（仙三外传/问情篇），运行时选择。约束：不得提交/推送。

---

## 1. 现状调研结论

### 1.1 上游机制（D:\Code\PAL3.Unity）
- 上游靠 **Unity 自定义编译符号** 切换两部游戏：菜单一键切换 Symbol + 图标 + 应用名 + 游戏数据文件夹（PAL3 ↔ PAL3A）。README 明确"两部游戏版本在项目中的切换是靠 Custom scripting symbols 实现的"→ **上游出的是两个包**。
- 数据文件夹约定：仙剑三必须叫 `PAL3`，外传必须叫 `PAL3A`（CPK 结构与大小写都不同）。

### 1.2 本仓库变体门控规模（实测）
| 位置 | 数量 | 说明 |
|---|---|---|
| Pal3.Game | 231 处 `#if/#elif PAL3A|PAL3`（122 文件） | 组件字段/接口列表/行为分支 |
| Pal3.Core | 48+ 处（SceCommands 59 处 + Constants） | 命令形状、路径常量、音乐表 |
| UnityEngine/Engine shims | 0 | 无变体门控（PAL3A 需要 FogMode 已补） |
| ManagedSharpDemo | 2 处 `#if PAL3` | 端口基础设施（driver spawn），与游戏变体无关 |
| UE C++/Config | 0 | 完全游戏无关（PAL3 只是字符串类别名） |

### 1.3 变体差异分类（决定方案可行性）
1. **常量表**（FileConstants/GameConstants/MusicConstants/LightingConstants）：`const string` 被门控（如 `ACTOR_FOLDER_NAME = "ROLE"` vs `"role"`、`AppName`、SceneCpkFileNames 集合、每场景音乐表）。→ 改 `static readonly` + 运行时变体即可，机械改动。
2. **数据驱动差异**：PAL3 场景 CPK 是 40 个独立包（Q01.cpk…），PAL3A 是 `SCN.CPK`+`SCE.CPK` 加 30 个小包；音乐/场景/世界地图表不同。→ 运行时按变体挂载不同 CPK 集合，GameResourceInitializer 里就是循环挂载，天然支持。
3. **类型形状冲突（8 个双形命令）**：`CameraPush/CameraMove/CameraRotate/UIDisplayCaption/FlyingActorFlyTo/SceneRotateWorld/PlayerSetActorLevel/ScriptEvaluateVarIsGreaterThanOrEqualTo` 同名类、PAL3A 多一个参数（如 `synchronous`）。**同名类不能共存于一个程序集** → 需要：重命名 PAL3A 变体（如 `CameraPushCommandA`）+ `SceCommandTypeResolver` 按运行时变体选表。解析器按 `(commandId, userVariableMask)` 查表，且参数个数由类属性决定（`Activator.CreateInstance(commandType, args)`），所以**同一 .sce 数据必须匹配对应变体的类形状**，不能用"属性并集"方案（会读错字节流）。
4. **PAL3A-only 游戏系统**：TaskManager、8 个小游戏（Appraisals/Sailing/HideFight/Encamp/Ski/SwatAFly/CaveExperience/GhostHunting）、PAL3A 专属命令执行器、UserVariableManager 额外接口。→ **并集编译**（两种游戏都编译进去）：PAL3 的 .sce 里不会出现这些命令 ID，纯死代码，无副作用；SaveManager 构造函数多传一个 TaskManager 实例也无害。**实测：无同名类型同时存在于两个变体（脚本扫描 0 冲突）**。
5. **行为分支**（GetMovementSpeed、初始场景/镜头、logo 路径、NpcInfoFactory、UserVariableManager 变量段）：纯运行时 if。
6. **PalScriptPatcher.PAL3.cs / .PAL3A.cs**：两个同名类（IPalScriptPatcher）→ 重命名 + 运行时选择。
7. **UE C++/配置**：零改动（已确认 Source/、Config/ 无变体门控；数据文件夹路径由托管侧 GameSettings/Pal3Boot 决定）。

### 1.4 数据文件现状（本机）
- `Steamapps/common/PAL3/`：完整安装（basedata/music/scene/snd/movie…）✓
- `Steamapps/common/PAL3A/`：**只有 save 文件夹，游戏数据未安装** ✗ → PAL3A 运行时验证被阻塞，需 Steam 下载/拷贝 PAL3A 原版数据后才能做 PAL3A 实机验证。

---

## 2. 方案对比

### 方案 A（终极）：去掉 PAL3A 宏，运行时 if/else
把全部 `#if PAL3/PAL3A` 改为运行时 `GameVariant.Current` 判断，单 DLL 双游戏。
- **优点**：真正单包单 DLL；启动时选游戏（甚至做"新游戏→选篇"）；后续上游同步直接带过来。
- **代价**：~380 个门控块 / 122 文件的迁移；8 个双形命令重命名 + resolver 改造（(gameType,id,mask) 三元键或按变体过滤注册表）；FileConstants/GameConstants 的 `const` → `static readonly`（注意 switch 语句中使用处）；`SceCommandTypeResolver.Init()` 时机必须在变体确定之后。
- **风险**：PAL3 当前 67 拍全对齐的已验证状态可能被触碰；**PAL3A 数据未安装，改完无法实机验证**；上游同步成本（上游新提交继续用 #if，需要持续手工迁移）。

### 方案 B（保守）：双托管 DLL 编译 + 单包运行时选择
保留 `#if` 结构；构建管线编译两遍（DefineConstants=PAL3 / PAL3A），打包进同一 APK；运行时按用户选择加载对应 DLL 集。
- **优点**：游戏代码零改动（PAL3 已验证状态不动）；上游同步零成本；风险集中在打包/加载管线（我们自己的 fork，可控）。
- **代价**：托管 DLL 双份（每变体 ~1.5MB，可忽略）；需要插件 fork 两处小改（双发布 + 加载目录选择）和安装后首次运行的"选择+重启"流程。
- **限制**：切换游戏需重启进程（加载期决定）。

### 推荐：**先 B 后 A**
B 先落地（解锁单包 + 双变体，且不影响已验证的 PAL3），A 作为长期演进在 B 的"选择器/数据路径"骨架上增量迁移（A 需要的 GameVariant 单例、变体选择 UI、数据路径切换在 B 中都已存在，迁移时逐文件替换 `#if` 为运行时判断即可）。

**共同阻塞项**：PAL3A 原版游戏数据未安装（Steam 库里有 PAL3A 但本机只有 save 目录）——两条方案都需要先拿到 PAL3A 数据（Steam 下载或拷贝原版目录为 `PAL3A`）才能做 PAL3A 实机验证。

---

## 3. 本次已实施（方案 B 基础设施 + 方案 A 地基，全部未提交）

### 3.1 变体参数化构建（已实现并双变体编译验证）
- `Script/Directory.Build.props`：新增 `PalVariant`（env `PAL3_VARIANT` → `-p:PalVariant` → 默认 PAL3），导出 `PalGameDefine`（=PAL3 或 PAL3A，**互斥**，匹配上游语义）。
- `Pal3.Core/Pal3.Game.csproj`：`DefineConstants` 由硬编码 `PAL3` 改为 `$(PalGameDefine)`。
- `ManagedSharpDemo.csproj`：改为 **无条件定义 `PAL3;PAL3A`**——该程序集是端口基础设施（driver/GameMode），其 `#if PAL3` 语义是"端口激活"而非"游戏是 PAL3"，双定义保证两种变体都会 spawn driver（该程序集无 `#if PAL3A` 段，安全）。
- `UnityEngine/UnityTypes.cs`：补 `FogMode` 枚举 + `RenderSettings.fogMode`（PAL3A 的 SceneEnableFogCommand 需要，PAL3 不用）。
- `Pal3.Game/Stubs/Pal3Boot.cs`：数据路径按变体（`GameDataFolderName`：PAL3/PAL3A；Steam 默认路径、Android `/sdcard/Download/<variant>`、iOS/Mac 路径与全部提示文案）。
- **验证**：`PAL3_VARIANT=PAL3A dotnet build` → 0 错误；PAL3 基线重建 → 0 错误；编辑器启动 + PIE（actors=120/scripts=68，无 HANG/无 Critical error）。

### 3.2 运行时变体选择（已实现并验证，插件仅改 2 个文件）
**设计（按用户要求：变体目录放 `Managed/<platform>/Variants/<game>/`，插件在 init 处广播一次，业务层决定托管目录）：**
- 插件 `CSManager.h/.cpp`：
  - 新增静态 `FOnResolveUserAssemblyDirectory`（`TMulticastDelegate<void(FString&)>`）与
    `UCSManager::OnResolveUserAssemblyDirectory()` 访问器；
  - `InitialAssemblyLoad()` 开头取 `Paths::GetUserAssemblyDirectory()` → **Broadcast** → 随后把
    **UserCode** manifest 的路径重定向到返回值（GlueCode 留在基础目录：与变体无关，且程序集名两边一致，
    故其顺序对变体同样有效）。名称匹配用 `StartsWith("UserCode")`（manifest.Name 是文件名主干）。
- 业务层（`Source/SharpDemo/SharpDemo.cpp`，Game 模块 Default 阶段，早于 UnrealSharpCore PostDefault）：
  订阅该委托，读 `Saved/Pal3Variant.txt` → `<user-dir>/Variants/<Variant>/`；目录不存在则回退基础集并告警。
  （**不用 GConfig**：`GConfig->Flush` 对 Config 层 ini 实测重启后丢失；`Saved/` 在 Android 也可写。）
- 选择入口：`Pal3DebugOverlay::SetPal3Variant/GetPal3Variant/IsPal3VariantAvailable`
  （UHT 把 Get/Set 对合并成托管属性 `Pal3Variant`）；主菜单新增"游戏版本"按钮，显示
  `PAL3 / PAL3A · 重启生效 / · 未安装`，中英文文案齐备。
- 打包：`SharpDemo.Build.cs` 把 `Content/Managed/<Platform>/Variants/*/{dll,pdb,json}` 全部登记为 UFS。
- `BuildAndroid.bat`：Step 2b 用 `PAL3_VARIANT=PAL3A` 再发布一次 → 拷到 `Variants/PAL3A` → Step 2c 重新发布
  PAL3 基础集（PackageProjectMono 的 `-p:PublishDir` 会覆盖工程级属性，故用"发布-搬运-再发布"而非直接改目录）。

**已验证（Win64 编辑器）**：
- `Variants/PAL3A/Pal3.Game.dll` 含 PAL3A 专有代码（`GhostHuntingMiniGame` 符号），基础 dll 不含；
- 启动日志：`LogPal3Variant: Pal3 variant 'PAL3A' active: .../Variants/PAL3A` →
  `User assemblies resolved from: .../Variants/PAL3A` → 6 个程序集全部从该目录加载；
- 启动数据路径变为 `E:\...\steamapps\common\PAL3A\`，CPK 挂载集合切换为 PAL3A（`SCN.CPK`/`sce.cpk`/`y01.cpk` 等）；
- 无 `Critical error` / `Failed to find type`。

### 3.2a 顺带修掉的长期遗留：PAL3 标题界面也是黑屏
用户确认：PAL3 的开始界面场景**一直**是黑的（只有场景音效），因为不阻塞流程一直没管。
根因与 PAL3A 完全相同——`DebugLogConsole.ExecuteCommand` 是空桩，`MainMenu.ShowInitView`
排队的 deferred 文本命令从未执行：

| 阶段 | PAL3 相机（heartbeat） | 画面 |
|---|---|---|
| 修复前（pal3_variant.log） | `(10381, 9457, 8994)` cm ≈ Unity (94.6, 89.9, 103.8) —— 停放在 (77,77,77) + 34.4 m 菜单轨道 | 全黑（离场景 ~150 m） |
| 修复后（pal3_avail.log） | `(1934, -1832, 1644)` cm ≈ Unity (-18.3, 16.4, 19.3) —— 在房间内做 20 s 轨道 | 景天房间正常显示 |

注：早前推断"PAL3 靠标题场景脚本 1511 自带 CameraSetTransform 所以正常"是**错误**的；
1511 在 offset 64 处是 `ScriptRunnerSetOperator`，并非相机命令。两个变体都只依赖 deferred 命令，
所以该修复同时修好两部游戏的标题界面。

### 3.2b 编辑器实机验证（PAL3A，2026-11-21）
数据已安装到 `E:\Program Files (x86)\Steam\steamapps\common\PAL3A\`，用 Remote Control 驱动 PIE 全流程验证通过：

| 阶段 | 证据 |
|---|---|
| 标题界面 | `state=Title`、`city=q02/scene=qn08y`（PAL3A 初始场景）、菜单 logo/标题按变体切换 |
| 开始游戏 / 片头 | `start` → `state=Cutscene`（script 51 init）→ `VideoPlaying`（pal3a Bink，`Skip` 可跳过） |
| 场景加载 | `Loading Q01/Q01`：构建场景网格 → 加载场景物件 → 完成，脚本 2001「春日下蜀山」接管 |
| 序章剧情 | 南宫煌开场台词（带头像对话栏）逐句推进，镜头切换正常，`Saved/Screenshots/pal3a_verify_prologue.png` |
| 进入玩法 | 连续 `advance` 后 `state=Gameplay`、`playerHasControl=true`、story=10101，`pal3a_verify_gameplay.png` |
| 稳定性 | 全程无 `Critical error` / `Fatal error` / 断言；PIE 世界 actor 数在场景加载后稳定 2869 |

**关于「actor 数持续增长」的疑云**：早期在标题界面观察到 ~0.8/s 增长，实测采样（`PAL3_TRACE_ENTITY=1`）
显示新增 actor 全是场景几何（`plane06`/`box03`/`cylinder146`/`Object_11_AutoTrigger` 等），
即加载 Q01 场景时按网格实例逐个 spawn，加载完成后计数恒定 → **不是实体泄漏**。
（对比 PAL3 标题界面 118 个 actor 只是标题场景比 PAL3A 的 q02 小。）

### 3.2c 新增自动化入口（编辑器专用，RC 可调）
合成鼠标/键盘输入对 UE 编辑器 Slate 不可靠（点 Play 按钮常被吞），改为引擎内入口：

| 函数 | 用途 |
|---|---|
| `UPal3DebugOverlay::StartPieSession` | `GEditor->RequestPlaySession(EPlaySessionWorldType::PlayInEditor)` 启动 PIE |
| `UPal3DebugOverlay::StopPieSession` | `GEditor->RequestEndPlayMap()` 结束 PIE |
| `UPal3DebugOverlay::FindActorPaths` | 返回 actor 完整对象路径（PIE actor 的 RC objectPath 形如 `/Game/PAL3/Maps/UEDPIE_0_LV_Pal3.LV_Pal3:PersistentLevel.Pal3Driver_C_0`） |
| `UPal3DebugOverlay::ExecConsoleCommand` | 无 GameViewport 时回退 `GEditor->Exec`，编辑器内也可执行控制台命令 |

驱动链路：RC `PUT /remote/object/call` → `StartPieSession` → `FindActorPaths("Driver")` →
对 driver actor 调 `ExecutePal3Command {"command":"start"|"advance"|"status"|...}`，无需任何合成输入。

### 3.2d 打包流水线两个致命缺陷（2026-11-21 实测修复）

Android 单包第一次真正跑通前，`BuildAndroid.bat` 有两个"每步都报 OK、包却是错的"的坑：

1. **变体被 2c 的清理删掉**（P0，静默丢功能）
   `PackageProjectMono` 每次发布都会先 `Cleaning existing output at 'Content/Managed/Android'`（整目录删除）。
   原流程 2b 发布 PAL3A → 从基础目录拷到 `Variants/PAL3A` → 2c 重新发布 PAL3 基础集，
   **2c 的清理把刚拷进去的 Variants 整目录删掉**，最终 APK 只有 PAL3，而日志里
   `[ OK ] PAL3A variant copied` 照常打印。
   证据：`Cleaning existing output` 出现 3 次（Step 2 / 2b / 2c），变体拷贝在 2b 与 2c 之间。
   修复：变体先发布到 **发布目录之外** 的暂存区（`Saved/ManagedStage/PAL3A`），
   等 2c 把基础集发布完，再由新的 **Step 2d** 安装进 `Content/Managed/Android/Variants/PAL3A`，
   并对 `Pal3.Game.dll` 做存在性校验（缺失直接 `[FAIL]`）。

2. **UBT 拒绝源=目标的 RuntimeDependency**（P0，构建直接失败）
   `SharpDemo.Build.cs` 里用两参数 `RuntimeDependencies.Add(dest, src, UFS)` 登记变体文件时，
   dest 展开后与 src 是同一个工程内文件：
   `FilePatternException: '.../Variants/PAL3A/Engine.dll' is listed as a source and target file`。
   插件自己的做法是单参数 `RuntimeDependencies.Add(<工程内绝对路径>, UFS)`
   （`DotNetRuntimeSDK.Build.cs` 的 `StageProjectManagedDlls`，且 glob 非递归，所以子目录必须自己登记）。
   修复：变体登记也改单参数形式，由 UBT 按工程相对路径推导 UFS 目标。

3. **验证方式**：APK 内容不在 `.pak` 文件里，而在 `assets/main.obb.png`（Epic 的混淆命名）。
   直接在该二进制里按 ASCII 搜 pak 索引字符串即可确认：
   `SharpDemo/Content/Managed/Android/Variants/PAL3A/Engine.dll` 存在；
   索引里同时可见 `UserCode.LoadOrder.json`（基础集）与变体目录条目。

### 3.2e 打包管线重构（2026-11-21 晚，单播 hook + 发布子目录 + 载荷剪裁）

上一版虽然打出了含双变体的包，但存在三个问题：插件 hook 是多播伪链式接口、打包要走
"发布-搬运-再发布"三次编译、变体目录把 87 MB 的 glue/BCL 又复制了一份（APK 多出约 30 MB）。
本次按"插件通用、项目策略"的分界重构：

**1. 插件 hook 改单播（通用）**
- `UCSManager::OnResolveUserAssemblyDirectory()`（多播 + `FString&` 出参）删除，改为
  `UCSManager::SetUserAssemblyDirectoryResolver(TFunction<FString(const FString&)>)`：
  输入基础目录、返回实际加载目录，**单所有者、无链式改写**；不设置时行为不变。
- 项目侧 `SharpDemo.cpp` 只需 `SetUserAssemblyDirectoryResolver(&ResolvePal3VariantDirectory)`
  （Startup 注册，Shutdown 传 nullptr），不再需要 delegate handle。
- 变体目录完整性同时校验 `Pal3.Game.dll`，缺失时回退基础集并**把标记写回 PAL3**，
  避免"菜单显示 PAL3A、实际加载 PAL3"。

**2. `PackageProjectMono -ManagedSubDir=<相对路径>`（通用）**
- 命令的输出目录可指向 `Content/Managed/<Platform>/<SubDir>`，清理也只清这个子目录
  （拒绝绝对路径与 `..`，并做全路径前缀校验）。
- `BuildAndroid.bat` 因此从"发布→搬运→再发布"变成**两次发布**：
  `PAL3 基础集` + `PAL3A (-ManagedSubDir=Variants/PAL3A)`；不再有"第二次发布把变体目录删掉"的坑。

**3. 变体载荷剪裁（项目策略 + 通用脚本）**
- 变体发布产物包含宿主自己加载的 glue（`UnrealSharp.dll` 38 MB + pdb）与 BCL 副本，
  这些由基础目录/插件目录解析，变体目录只需要 **UserCode manifest 里那 6 个程序集 + pdb**。
- `Script/Tools/prune_managed_variant.ps1` 按平台根目录的 `UserCode.LoadOrder.json` 剪裁
  （缺 dll 直接失败），并把 manifest 复制到变体目录自描述。
  实测：`pruned 87.6 MB → 1.60 MB`；APK 从 274 MB 降到 **244 MB**（pak 索引确认
  `Variants/PAL3A/` 下只有 6 个程序集，不再有 `UnrealSharp.dll`）。
**5. 设备实测发现的关键坑：按名解析会绕过 manifest 重写**
Android 日志显示：`Engine.dll` 从变体目录加载后，Mono preload hook 立刻按**程序集名**解析
`Pal3.Game.dll`，而搜索路径首项是平台根（`RuntimeDir` = `Content/Managed/Android`），
于是基础集（PAL3 版）先命中并成为"规范"程序集；变体版随后按 manifest 路径加载成第二份，
但类型绑定已指向基础集 → 表现为 `Pal3 variant 'PAL3A' active` 但 `Pal3Boot` 仍用 PAL3 数据目录。
修复（插件通用能力）：`UCSManager::GetResolvedUserAssemblyDirectory()` 暴露解析后的目录，
`InitializeMonoRuntime` 新增 `PrioritySearchPaths`（排在 `RuntimeDir` 之前、自动去重），
Mono host 把解析目录作为优先搜索路径。验证：`mono_set_assemblies_path` 变为
`.../Variants/PAL3A:.../Managed/Android`，`UFS-loaded assembly: .../Variants/PAL3A/Pal3.Game.dll`，
`Full boot starting, game data folder: /sdcard/Download/PAL3A`。
BCL/glue 仍从平台根解析（变体目录里没有它们），与"BCL 绑定 native 运行时"的约束一致。

**6. 菜单 logo 清晰度**
运行时纹理 1024x1024、logo 裁切 500x253、`mips=1`（日志 `[JsonImage]` 可查）。
原菜单把 500x253 塞进 286x116 → `UScaleBox(ScaleToFit)` 缩放约 0.46，Slate 走 render-transform
路径后细节明显损失（对比 loading 的 650x252 元素几乎 1:1）。修复：不改采样、改为**按原生尺寸画** ——
logo 从面板内挪到画布左上角 `anchor=top_left position=[24,12] size=[500,253]`，
与 loading 同尺寸，清晰度一致。变体按钮同时加宽到 812，避免"PAL3A · 重启生效"文字溢出。

**边界（重要）**：BCL 与 native 运行时（`libmonosgen-2.0.so`/CoreCLR）是按 platform+arch 绑定的一套，
不能脱离 native lib 单独按变体切换——所以 BCL 永远留在平台根（`Content/Managed/<Platform>/`，
编辑器则在 `DotNetRuntimeSDK/Mono/<Platform>/<arch>/runtime/`），变体只承载业务程序集。
`prune_managed_variant.ps1` 会断言 manifest 里不含框架程序集（`System.*`/`Microsoft.*`/`mscorlib`/
`netstandard`/`UnrealSharp*`），防止把 BCL/glue 误塞进变体、与随包 native 运行时配错。

- 编辑器侧同理：`Script/Tools/PublishEditorVariant.ps1`（`dotnet publish -p:UETargetType=Editor
  -p:PublishDir=Binaries/Managed/<tfm>/Variants/<V>` + 同一剪裁），供 PIE 验证变体。

**4. 顺带修复**
- 菜单 logo 第二次渲染错误：缓存分支曾用整图 UV `(0,0,1,1)`，而 logo 是图集子矩形
  （PAL3 `(0,600,375,145)` / PAL3A `(0,770,500,253)`）→ 现在 UV 随纹理一起缓存
  （菜单每次重开/刷新都会走缓存分支）。`ResetStatics()` 挂到 driver 的会话重置。
- `GameConstants.IsPal3A` 取代 `AppName` 字符串比较。
- 启动失败页新增"游戏版本"按钮：**只有 PAL3A 数据的安装**也能在此切到 PAL3A（重启生效），
  不再因为默认 PAL3 数据缺失而无路可走。
- 自动化新增 `refresh-menu` 命令（RC 可调，等价于按钮点击后的 Show），用于无合成输入地
  验证菜单刷新；`Pal3Variant` 写入加了 allow-list（非法值规范成 PAL3）。

### 3.2f 待完成
- PAL3A 音乐 / TaskManager 系统在玩法内的进一步验证。

### 3.2g 审查修复后的实现（2026-11-21）

- 插件以 `mobile20261120` 的 `bae77065` 为基线，新建本地 `pal3a` 分支。通用变体接口移到该基线，保留已有的参数生命周期、TSet 元素析构和世界上下文修复。
- `SetUserAssemblyDirectoryResolver` 在宿主初始化前注册，解析结果在当前运行时内固定。Mono 优先从选择的用户程序集目录解析，CoreCLR 移动宿主将对应程序集写入物理缓存。BCL 和引擎 glue 继续使用平台基础集。
- Android 使用基线自带的 `PackageProjectMobile`，通过新增的 `-ManagedSubDir=Variants/PAL3A` 进行第二次发布。任何一次发布或裁剪失败都会使打包失败。变体依赖图 `.deps.json` 随应用程序集保留，BCL/glue 不随变体重复发布。
- Cook 使用启动前已构建的程序集，避免在外层打包 UAT 运行期间再次启动 UAT，造成 AutomationTool 文件占用冲突。
- PAL3 存档沿用 `Saved/Saves`，PAL3A 使用 `Saved/PAL3A/Saves`。两者各有独立的自动存档和 99 个手动槽。
- 存读档回归还发现了格内位置偏移。恢复阶段在原版脚本命令完成导航格定位后，应用存档中的精确 X/Z；回归工具使用保存操作自身返回的快照进行比较。
- 转码视频分别缓存到 `Saved/Movies/PAL3`、`Saved/Movies/PAL3A`。旧的 `Saved/Movies/*.mp4` 没有可靠的版本归属，保留在原处，后续播放会在对应版本目录内重新转换。
- iOS 默认数据目录分别为 `Documents/PAL3`、`Documents/PAL3A`。通用的命名存储授权 API 为两部游戏保存独立书签，PAL3 继续兼容原有授权。PAL3A 选择器校验 `scene/SCN.CPK`，避免将 PAL3 目录当成外传数据。
- 变体 UFS 登记按当前目标平台收集，包含 Mac。清除 JSON UMG 图片时允许传入空纹理，Verbose 日志同样处理空纹理。
- Windows 的构建与 RC 回归记录保存在本机 `Saved/Review/PAL3A-fixes`。iOS 目录授权仍需在 Apple 平台验证。
- 本轮验证通过了编辑器原生构建、29 项生成器测试，以及两种编译版本的启动、剧情点进入和存读档。两部游戏分别保存 97 号槽，PAL3 原有槽位在 PAL3A 测试前后校验值一致；同名 `1c` 视频在两个版本目录生成了不同的 MP4。开启 Verbose 后向 JSON UMG 图片传空纹理也通过了 RC 回归。
- Android 使用 NDK 28 完成原生编译、Cook 和打包。最终 APK 中两套共 12 个应用 DLL、9 份 JSON 布局和 7 档仙剑三图标均已核对；从 APK 提取的 Core/Game DLL、PDB 与验证过的发布产物逐字节一致。
- Android 实机通过了两种版本启动、移动与镜头命令、99 号槽跨场景存读档、同名 `1c` 视频独立转码/播放/跳过，以及从新游戏开场推进到可操作状态。装机阶段补齐了问情篇新游戏入口的初始任务 `071` 登记，与上游入口保持一致，已通过首段剧情后的存档内容验证。
- Android 的文件自动化可通过 `-Pal3AutomationDir=<设备可写绝对目录>` 启用，调用同一游戏命令入口。设备回归记录在本机 `Saved/Review/PAL3A-device-20261121-200707`。设备原共享目录内确认属于问情篇的自动存档已迁至 `Saved/PAL3A/Saves`，保留 PC 备份并核对字节一致，测试槽位已清理。
- 桌面显示旧 UE 图标、任务栏显示仙剑图标的差异来自小米桌面缓存，重启桌面后已由设备截图及用户确认恢复一致。

### 3.3 方案 A 的迁移清单（供后续研究）

当前使用两套编译结果，继续沿用上游的条件编译。下列方案保留为后续研究。
1. `Pal3.Core` 新增 `GameVariant` 单例（枚举 PAL3/PAL3A + Current + 数据文件夹名）。
2. FileConstants/GameConstants `const` → `static readonly`（switch 表达式使用者同步改）。
3. 8 个双形命令：PAL3A 版改名 `*CommandA`（或 `Pal3A` 命名空间子目录），`SceCommandTypeResolver.Init()` 按 Current 过滤注册。
4. PalScriptPatcher 两个文件改名 + 运行时选择。
5. 逐文件把 `#if PAL3/PAL3A` 换成 `GameVariant.Current == ...`（常量表/行为分支），并集编译变体专属系统。
6. 回归：PAL3 67 拍核对 + PAL3A 初始场景实机（需数据）。

---

## 4. 风险与注意事项
- 方案 A 迁移期间**不要**同时动渲染/相机等已验证模块，避免回归叠加。
- 双变体并集编译会让 PAL3 也注册 PAL3A 命令执行器与 TaskManager 服务——需确认 PAL3 存档/脚本不含这些 ID（.sce 按 ID 分发，PAL3 脚本不存在 PAL3A ID 即安全；上游正是靠宏排除，并集化后要靠"数据里没有"这个事实）。
- PAL3A 数据文件夹大小写敏感项（role/effectscn/cbscene）由 FileConstants 运行时切换覆盖；CPK 内部路径大小写是否敏感取决于 CpkFileSystem 实现，移植时已按原样处理。
- 持久化数据必须按编译版本隔离。上游独立应用的保存目录语义不能直接套用到单安装包，当前目录规则见“审查修复后的实现”。
