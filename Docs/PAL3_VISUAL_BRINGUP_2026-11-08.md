# PAL3 → UE5 画面/角色/对话 UI 移植记录（2026-11-08）

本文记录本轮"画面效果不对"排查中定位到的根因、修复方式与验证数据，供后续接续。
项目路径：`D:\UEProject\SharpDemo`（源码 UE5）、参考实现 `D:\Code\PAL3.Unity`。

---

## 1. 结论速览

| 现象 | 根因 | 修复 |
|---|---|---|
| 角色不在场景里（床上没人/两人重迭在原点的残留） | `GameEntity.SetParent` 把组合实体传给 `UEGameEntity.SetParent`，后者类型判断失败直接 return，**所有子实体从未 attach 到父 actor** | 组合实体解析出父的 `_ue` 再 attach |
| 角色全黑 | `Pal3.Game` 程序集里残留 `Engine.DataLoader.TgaTextureLoader` 桩（返回 1x1 白纹理），同程序集同名类型优先于 Engine 程序集的真实解码器（CS0436）→ 所有角色 `.tga` 无 UE 纹理 | 删除桩类 |
| 对话头像倒立、位置/尺寸不对 | 头像 TGA 为 bottom-left origin（descriptor byte17 bit5=0），解码后未翻转；且贴图带大量透明边 | 解码时按 descriptor 翻转行；managed 求 alpha bbox，按裁剪 UV 绘制 |
| 整体过亮、灰蓝（Unity 是暗暖色） | Unity 端是 HDRP + RealToon，整体比原始贴图暗约 5x（线性）且偏暖；UE 端材质是 unlit，无光照模型 | `UnityEngine.SceneLighting.SurfaceScale`（ambient×0.05 + light×0.03，再乘暖白平衡）乘到所有材质 TintColor |
| 调试日志面板挡住半个画面 | 侧栏默认可见 | 默认隐藏；F1 / RC `OverlayVisible` 打开；底部保留原生对话栏 |
| 场景背景是蓝天 | 关卡里有 SkyAtmosphere/HeightFog/SkyLight/强白平行光 | 删除这些 actor；材质全部 unlit，背景纯黑 |
| 光环类模型显示为暗环 | 透明/加法混合材质未实现 | 材质四态：`M_Pal3Tex`(opaque) / `_Add`(additive) / `_Alpha`(translucent) / `_Masked`(cutout) |
| 第二次 PIE 无菜单/直接旧台词，随后崩溃 | 托管运行时跨 PIE 不卸载：boot 标志、单例、协程、命令注册表都指向已销毁 actor | `Pal3Boot.ResetForNewSession()`（含 Singleton/CommandExecutorRegistry 重置），PIE 连续启停验证 |
| 游戏时间 2 倍速、动画快于移动（滑步）、脚本等待减半 | `Pal3Game` 每帧被 Update 两次（活跃表 + 额外反射钩子） | 删除额外钩子，Update/LateUpdate 分两遍各一次；10s 等待恢复为 10.003s |
| 编辑器崩溃 "Cannot queue the Expression Cache ... about to be deleted" | 池化 MID 以 TransientPackage 为 Outer，GC/清理与渲染线程竞态 | 所有 MID/纹理 `AddToRoot`，材质池清空改绑共享白纹理 |

像素抽样对照（同一相机、同一剧情拍）：

| 区域 | UE | Unity |
|---|---|---|
| 墙面 | (33,32,20) | (38,38,24) |
| 地面 | (20,18,14) | (17,18,17) |

---

## 2. 关键实现点

### 2.1 实体层级
- `GameEntity`（组合）持有 `_ue`（真实 `UEGameEntity`）。
- `SetParent` 必须把父的 `_ue` 传给 `UEGameEntity.SetParent`，否则 `parent is not UEGameEntity` 导致静默失败。
- 场景几何是"世界坐标直接写 actor"，掩盖了该 bug；角色模型用父空间坐标，因此全部塌在原点。

### 2.2 纹理链路
- 场景 CVD/POL 多用 `.dds`（DXT1/DXT3），角色与头像多为 `.tga`。
- `TextureProvider.GetTexture` 先试 `.dds`，失败回退原扩展名。
- **TGA origin**：byte17 bit5=0 → 行序自底向上，解码后需垂直翻转（头像已因此倒置）。
- **TGA 桩类**：检查 `grep -rn "class TgaTextureLoader" Script` 只应有一份（Engine 程序集）。
- RGBA → `UTexture2D::CreateTransient(..., TConstArrayView64<uint8>)`，`SRGB=true`，`NeverStream=true`。

### 2.3 材质
`/Game/Pal3/` 下三个材质，参数统一：

| 材质 | Blend | 用途 |
|---|---|---|
| `M_Pal3Tex` | Opaque | 常规场景/角色 |
| `M_Pal3Tex_Add` | Additive | 光晕/加法特效 |
| `M_Pal3Tex_Alpha` | Translucent | 半透明（水、雾、UI 贴片） |

参数：`Pal3Tex`（主纹理）、`ShadowTex`（UV1 阴影/光照贴图）、`TintColor`、`HasShadowTex`、`ShadowExposure`（默认 0.4）。
图：`Emissive = Pal3Tex * TintColor * lerp(1, ShadowTex/(1-Exposure), HasShadowTex)`；透明材质额外 `Opacity = tex.A * tint.A`。

C++：`UPal3AssetHelper::CreatePal3MaterialBlend(int32 0/1/2)`、`BindTexture(Material, FName, UTexture2D)`、`SetScalarParameter`、`SetVectorParameter`。

Managed：
- `UnityMaterial` 保留创建时的 shader 名（clone 池继承），`Fade/Transparent` → alpha 材质；
- `_BlendDstFactor == One` → additive；`OneMinusSrcAlpha` → alpha（延迟重建 MID 并重绑纹理/Tint）；
- `_TintColor`、`_HasShadowTex`、`_Exposure` 映射到上面参数；
- `ApplyTint()` = 游戏 Tint × `SceneLighting.SurfaceScale`。

### 2.4 对话栏（原生 HUD）
- `APal3DebugHUD::DrawDialogueBar`：底部 86% 宽、0.23 屏高、深色底 + 金色细边；
- 头像：`SetDialogueAvatar(UTexture2D*, bRightAligned, U0,V0,U1,V1)`，UV 是 alpha bbox（避开透明边），按裁剪比例缩放；
- 文本：中文逐字换行，字号 scale 1.85；
- 对话结束 `DialogueManager.ResetUI()` → `ClearDialogue()` + `ClearDialogueAvatar()`；
- 侧栏日志默认关闭，F1 / RC `SetOverlayVisible` 切换。

### 2.5 场景光照（近似）
`GameScene.SetupEnvironmentLighting` 末尾调用 `UnityEngine.SceneLighting.Set(mainLight.color, intensity, ambient, ambientIntensity)`。
`SurfaceScale = ambient*ambientIntensity*0.05 + mainLight*intensity*0.03`，再乘暖白平衡 `(1.20, 1.10, 0.62)`。
这是对 RealToon + HDRP 的**经验逼近**，不是物理正确；换场景（白天/室外）时可能需要重新标定。

---

## 3. 已知崩溃：MID 表达式缓存删除竞态（已修复）

```
Fatal error: [MaterialRenderProxy.cpp:664]
Cannot queue the Expression Cache for Material MID_M_Pal3Tex_11344 when it is about to be deleted
```

- 含义：渲染线程仍在排队使用某个 `UMaterialInstanceDynamic` 的 uniform 表达式缓存，而该对象已被 GC/transient 清理。
- 诱因：每个 `UnityMaterial` 都新建 MID（对象池 3500+500、克隆、混合模式切换重建），且反复 PIE 启停后累计上万；MID 以 `GetTransientPackage()` 为 Outer，托管侧只持有包装，GC/停止 PIE 时被清理，渲染线程来不及 flush。
- 修复（`Pal3AssetHelper.cpp`）：
  - 新增 `GPal3RootedAssets` + `RootPal3Asset()`，对 **所有** `CreateTransient` 纹理和 MID 调用 `AddToRoot()`，生命周期与进程一致；
  - `GetAssetStats()` 增加 `rooted=` 计数；
  - managed 侧 `SetTexture(null)`（材质池复用重置）改为绑定共享 1x1 白纹理，避免池化材质残留上一个对象的贴图。
- 验证：连续 3 次 PIE 启停 + 剧情运行，`Expression Cache` / `Critical error` 计数均为 0，编辑器进程保持响应。

> 注意：Root 会让旧会话的 MID 常驻，长时间开发会话内存会增长（每会话约 4000 个池化 MID）。后续可改为"按会话强引用容器 + 停止 PIE 后延迟释放"。

---

## 3.5 角色 UV 修复（纹理行序/采样约定）

- 现象：角色脸部/眼睛错位（眼睛跑到胸口）、衣服贴图错块；场景贴图正常。
- 事实：`Mv3`（角色）贴图走 TGA 路径，`Cvd`（场景物件）多走 DDS 路径；两条路径的**上传行序与 Unity 采样约定不一致**，因此不能用同一个 V 翻转。
- 当前实现（业务侧分开处理，作为兜底）：
  - 全局 mesh 上传 **不翻转 UV**（保持场景正确）；
  - `Mv3ModelRenderer` 在把 UV 写入 `UvBuffer` 后做 `v = 1 - v`（角色正确）；
  - TGA 加载按 descriptor bit5 翻转行（头像等 HUD 直接绘制正确）。
- 根因 TODO（建议后续统一）：确认 Unity 导入 DDS/TGA 时的实际行序（Unity 纹理内部通常是自上而下），然后统一成"**纹理全部按 UE 行序上传 + 所有 UV 统一翻转**"或"**按 Unity 行序上传 + 不翻转 UV**"两种一致模型，删除 Mv3 特判。当前 Mv3 特判是已验证可工作的兜底。

---

## 3.6 其它材质修正

- 增加 `M_Pal3Tex_Masked`（BLEND_Masked，OpacityMask = tex.A × tint.A，Clip 0.3），用于 Mv3 头发/衣物 alpha 裁切（游戏 `_Cutout=0.3`）；`UnityMaterial.SetFloat("_Cutout")` 自动在 opaque/masked 间切换。
- 混合模式扩展为 4 态：0 opaque / 1 additive / 2 translucent / 3 masked。

---

## 5. 角色动画"滑步/不播放"（2026-11-08 续）

### 现象
- 雪见走路时脚不动（滑步），说"好好玩"时没有拍手动作；景天同样。
- 日志里动作确实切换了（`c02` 行走、`j05/j06` 等），但看起来没动画。

### 根因：编辑器后台限速导致 PIE 只有 ~3 FPS
- heartbeat 帧计数显示 **2 秒只有 6 帧（3 FPS）**，`deltaTime = 0.333 s`（Unity `Time.maximumDeltaTime` 默认值）。
- 行走逻辑 `MoveTowards(speed=4.0 * deltaTime)` 一步 1.33 m，而相邻 tile 间距 ~1.2 m → **一帧走完整条路径**：`c02` 只持续 7 ms 就被 `c01` 替换，动画来不及播；连续多步时表现为"滑步"。
- 该 3 FPS 不是性能问题：`stat unit` 显示 Frame 10.11 ms、Game 10.11 ms、Draw 3.85 ms。原因是 **UE 编辑器在窗口失焦时默认降 CPU（bThrottleCPUWhenNotForeground=true）**，自动化验证时编辑器不在前台。


### 真正的根因（2026-11-08 续二）：Pal3Game 每帧被 Update 两次
- 现象补充：`[108] ScriptRunnerWaitUntilTime {"Time":10.0}` 到 `[140]` 只用了 **5.0 s**（应为 10 s）。
- 原因：`Pal3Game` 作为普通组件被 `GameEntityScriptRuntime.Activate` 注册进 `_active`，反射缓存能找到它的 `private Update/LateUpdate`，每帧循环会调用一次；同时早期为了"让 Pal3Game 动起来"加的 `WireGameLoopHooks` 反射钩子又调用一次 → **Update/LateUpdate 每帧执行两遍**。
  - 结果：`GameTimeProvider.TimeSinceStartup`、`ScriptManager`、`DialogueManager` 全部 2 倍速；角色动画按 2 倍速播放，而 ActorMovementController 只按真实 delta 移动一次 → **脚滑**；一次性动作（起床/拍手）2 倍速一闪而过，看起来"没有动画"；所有脚本等待时间减半。
- 修复：删除 TickAll 里额外的 `_pal3Update/_pal3LateUpdate` 调用，`WireGameLoopHooks` 改为 no-op（保留调用点）；同时把 `_active` 循环拆成 Unity 语义的两遍——先全部 `Update`，再全部 `LateUpdate`（相机跟随能看到本帧所有位移）。
- 验证：10 s 等待现在精确 10.003 s；60 FPS；`[378] z19` / `[839] ActorPathTo` 的动画与位移同步。
- **状态（2026-11-08 提交时）**：2 倍速问题已修，但用户复测仍觉得动作视觉不正确（滑步/下床动作）。剩余怀疑点：Mv3 动作数据的 tick/帧率换算、动作切换时的帧插值、移动速度与动作步幅匹配。后续继续排查。

### 结论与最终配置
- **用户决定：保留引擎默认的后台降速**（编辑器失焦时 ~3 FPS 属预期行为，不再改 `bThrottleCPUWhenNotForeground`）。知道原因即可；需要在失焦窗口观察时，接受画面按 3 FPS 播放。
- 编辑器/PIE 帧率上限 60（用户要求）：`Config/DefaultEngine.ini`
   ```ini
   [/Script/Engine.Engine]
   bUseFixedFrameRate=True
   FixedFrameRate=60.000000
   ```
- **低帧率下的正确性处理（已实现）**：`GameEntityScriptRuntime.TickAll` 对模拟 delta 做上限钳制（默认 1/20 s，可用环境变量 `PAL3_MAX_SIM_DELTA` 覆盖，<=0 关闭），并把钳制后的 delta 同时用于 `Time.*`、协程、`OnUpdateGameEntity/OnLateUpdateGameEntity`。
  - 效果：低帧率时进入**时间膨胀**（游戏时间慢于墙钟），每帧位移仍与 delta 成比例，行为逻辑（行动时长/动画帧推进）按模拟时间保持一致；不会出现"一帧走完整条路、动作 7ms 被替换"。
  - 代价：3 FPS 时画面本身仍是每秒 3 张，只能做到"每帧一步"的定格连贯，无法像高帧率一样平滑；这是渲染帧率的物理下限。
  - 正常帧率（>=20 FPS）无影响。
- 验证：60 FPS 时 heartbeat 每 2 秒 ~121 帧、`t` 每 2 秒 +2.0s（钳制未触发）；3 FPS 时 `t` 每 2 秒约 +0.3s（6 帧 × 0.05s），行走按模拟时间推进。

> 如果只在"失焦窗口"里截屏观察，仍可能看到 3 FPS 的表现；请聚焦编辑器 PIE 后确认。

---

## 6. 音频（WAV + MP3，无外部插件）

- **不依赖 RuntimeAudioImporter**：UE5 自带 `Engine/Source/ThirdParty/dr_libs`（dr_libs v0.6.39，含 `FMP3Decoder`），模块加 `dr_libs` 依赖即可运行时解 MP3；WAV 用 ~60 行 RIFF 解析。
- 新增 `Source/SharpDemo/Pal3AudioHelper.{h,cpp}`：
  - `PlayAudio(bytes, audioType, volume, loopCount) -> handle`（7=WAV / 6=MPEG，Unity AudioType 值）；
  - WAV：PCM8/16/float32 → PCM16；MP3：解析帧头（跳过 ID3v2、按 bitrate/samplerate 表切帧）逐帧喂 `FMP3Decoder`；PCM16 交给 `USoundWaveProcedural::QueueAudio` + `UGameplayStatics::SpawnSound2D`；
  - `TickAudio()` 负责循环实例重放（音乐 `loop=-1`）、`StopAudio/StopAllAudio/SetAudioVolume`；
  - 所有 ProceduralSoundWave `AddToRoot()`，避免 GC/渲染线程竞态。
- managed：
  - `GameResourceProvider.LoadAudioClipAsync` 直接读文件（SFX 在 `snd/*.wav`；音乐由 `ExtractAndMoveMp3FileToCacheFolderAsync` 从 `music.cpk` 解出），WAV header 计算 `length`；
  - `AudioSource` 的 `Play/Stop/PlayOneShot/volume` 接到 C++ handle；
  - `APal3Driver.Tick` 调 `TickAudio()`；会话重置时 `StopAllAudio()`。
- 验证：日志 `[Pal3Audio] play handle=1 type=7 22.0kHz x1`（SFX）、`type=6 44.1kHz x2 loops=-1`（PI27 音乐），`audio=decoded=..` 计数增长。听感需人工确认。

---

## 7. RuntimeWidgetLayout 模块（JSON → UMG）

新模块 `Source/RuntimeWidgetLayout`（Runtime，Default 加载）：

- `URuntimeJsonWidget : UUserWidget`：`BuildFromJson(FString)` 解析 JSON 并用 `UWidgetTree->ConstructWidget` 构造控件树；
- 已支持：`canvas`、`border`、`text`、`button`、`progressbar`（可扩展）；
- `URuntimeJsonWidgetLibrary`：
  - `CreateJsonWidget(Json, ZOrder)` → 建树 + `AddToViewport` + `GameAndUI` 输入模式 + 鼠标光标；
  - `RemoveJsonWidget` / `SetJsonWidgetVisible`；
- 按钮点击：`UJsonButton`（记录 JSON `id`）+ `ConsumeClickedButton(out id)` 轮询；`NativeTick` 里另有按 PC 鼠标坐标 + 按钮矩形的手动命中测试兜底；
- C# 用法（`Script/ManagedSharpDemo/JsonUiDemo.cs`，RC `RequestJsonDemo` 触发）：JSON → `URuntimeJsonWidgetLibrary.CreateJsonWidget(json, 100)` → 每帧 `ConsumeClickedButton` 取 id；
- 已验证：PIE 中 JSON 面板正确渲染（`Saved/Screenshots/pal3_jsonui.png`）；无前台焦点的自动化环境无法注入 Slate 点击（PC 鼠标坐标冻结），按钮点击需人工确认；绑定与轮询逻辑已就绪；
- JSON 坐标为设计空间像素（1080p 基准，UMG DPI 曲线会缩放到实际视口）。

---

## 8. 仍未完成

- 角色/场景脚下无影子（`SpriteRenderer` 仍是数据桩；需 UE 投影 quad 或 blob shadow）。
- 两处光环 artifact 的正确混合语义待确认（已具备 additive/alpha 材质，但对应对象可能走其他路径）。
- 开场视频（`pal3op` 缺失，VideoManager 只记错误日志）。
- JSON UI：按钮点击在自动化环境未验证（需人工点击确认）；Slate 输入路由在失焦 PIE 下不喂事件，已加手动命中兜底。
- 完整 UI（存档/菜单/选项）尚未迁移到 RuntimeWidgetLayout。
- 字体排版与原文更精细的对齐。
- UV 约定统一（见 3.5 的 TODO）；MID 内存增长治理。

---

## 9. 复现与验证

```
# 编译 C++（会顺带生成 UHT glue）
D:\Code\UE5\Engine\Binaries\ThirdParty\DotNet\10.0\win-x64\dotnet.exe \
  D:\Code\UE5\Engine\Binaries\DotNET\UnrealBuildTool\UnrealBuildTool.dll \
  SharpDemoEditor Win64 Development -Project=D:\UEProject\SharpDemo\SharpDemo.uproject -WaitMutex

# 编译托管（注意引用项目会编到各自 bin\Debug，最终 DLL 在 app 的 bin\Release）
cd D:\UEProject\SharpDemo\Script
dotnet build ManagedSharpDemo.sln -c Release -p:EnablePal3=true
cp ManagedSharpDemo\bin\Release\net10.0\{Engine,Pal3.Game,ManagedSharpDemo}.dll ..\Binaries\Managed\net10.0\

# 启动编辑器 + RC 启停 PIE
UnrealEditor.exe D:\UEProject\SharpDemo\SharpDemo.uproject -log -abslog=...\Saved\Logs\pal3.log
python Script\Tools\ue_remote.py call /Script/LevelEditor.Default__LevelEditorSubsystem EditorRequestBeginPlay
python Script\Tools\ue_remote.py call /Script\SharpDemo.Default__Pal3DebugOverlay StartGame
```

关键日志标记：`[Pal3Boot] Session state reset`、`Full boot starting`、`Scene loaded`、`[Lighting]`、`[Pal3Driver] actors=`（含 a11/a12/a13 位置与 native 位置）。
