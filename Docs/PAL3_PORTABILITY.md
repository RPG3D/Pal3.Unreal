# PAL3 上游代码跨引擎化改造建议

> 这是一份 2026-11-06 的架构提案，文中的统计、工期和清理清单属于当时的估算。当前优先交付可用的 UE 移植，再减少与 PAL3.Unity 的差异。玩家与 NPC 已共用上游移动逻辑，朝向在 wrapper 处理。当前入口和待办见[项目 README](../README.md)。

> 目标：让 `Pal3.Core` + `Pal3.Game` 的游戏逻辑**不依赖任何引擎**，同一份代码既能在 Unity（上游）里跑，也能在 UnrealSharp/UE 里跑。
> 本文只做设计与清单，不含代码改动。
> 依据：对上游 `D:\Code\PAL3.Unity\Assets\Scripts` 与移植侧 `D:\UEProject\SharpDemo\Script` 的实测统计（2026-11-06）。

---

## 0. 先说结论

上游的架构底子**比自己以为的好**：它本来就有一个 `Engine` 抽象层（`IGameEntity` / `ITransform` / `ITexture2D` / `IMaterial` / `IPhysicsManager` / `ServiceLocator`），
而且游戏逻辑已经在大量使用它——**PascalCase 的 `.Transform` 有 186 处，Unity 的 `.transform` 只有 8 处**。
所以跨引擎化不是重写，而是**把剩下 8 类"直接摸 Unity"的地方收口到已有的抽象里**。

真正花钱的地方只有一处：`GameEntityScript : MonoBehaviour` —— 它把整个游戏逻辑绑在 Unity 的基类上（约 219 个文件里的绝大多数派生自它）。

| 指标 | 现状（上游） |
|---|---|
| `Pal3.Core` 含 `using UnityEngine` 的文件 | **0 / 293**（已经完全可移植） |
| `Pal3.Game` 含 `using UnityEngine` 的文件 | **46 / 219** |
| `Pal3.Game` 含任何 Unity 依赖（含 MonoBehaviour/SerializeField）的文件 | **111 / 219** |
| `GameEntityScript` 派生类 | 几乎全部 gameplay 脚本 |

改造完成后，衡量标准很硬：

```
grep -r "UnityEngine" Pal3.Core Pal3.Game --include=*.cs   →  0 结果
两个程序集不再引用 UnityEngine 程序集
```

---

## 1. 分层目标（三层，边界清晰）

```
┌───────────────────────────────────────────────────────────┐
│ Pal3.Core       纯数据/命令：CPK、LZO、Mv3、Cvd、Scn、Sce   │  零引擎依赖（现状已达成）
├───────────────────────────────────────────────────────────┤
│ Pal3.Game       游戏逻辑：场景/角色/镜头/战斗/对话/脚本      │  只依赖 Engine.Abstraction
├───────────────────────────────────────────────────────────┤
│ Engine.Abstraction  契约：IGameEntity/ITransform/IMaterial/ │  零引擎依赖（需从 Engine 里剥出来）
│                     ITexture2D/IPhysicsManager/ICoroutine…  │
├───────────────────────────────────────────────────────────┤
│ 后端（Backend）  UnityBackend  |  UnrealBackend             │  唯一允许 `using UnityEngine` 的地方
└───────────────────────────────────────────────────────────┘
```

**关键结构调整（Tier 0，机械操作，不碰逻辑）**

把上游的 `Engine` 程序集一分为二：

| 新程序集 | 内容 | 依赖 |
|---|---|---|
| `Engine.Abstraction` | `Engine/Core/Abstraction/*`、`Engine/Services/I*.cs`、`Engine/Animation` 的纯数据、`Engine/Navigation` | 无（连 UnityEngine 都不引） |
| `Engine.Unity` | `Engine/Core/Implementation/Unity*`、`GameEntityScript`、`UnityEngineUtility`、`ShaderUtility` | UnityEngine + Engine.Abstraction |
| （移植侧对应）`Engine.Unreal` | `UETransform` / `UEGameEntity` / `UEGameEntityFactory` / `UnityUeConvert` | UnrealSharp + Engine.Abstraction |

这一步不改任何游戏逻辑，却让"哪层能碰引擎"从口头约定变成编译期约束。

---

## 2. 八类泄漏与微调方案

按"上游要改多少行"排序，**从最划算的开始**。

### L1 ⭐ `GameEntityScript` 继承 MonoBehaviour（最高杠杆，影响全部 gameplay）

现状（`Engine/Core/Implementation/GameEntityScript.cs`）：

```csharp
public abstract class GameEntityScript : MonoBehaviour   // ← 唯一把逻辑绑死 Unity 的地方
{
    public ITransform Transform => new UnityTransform(base.transform);
    public IGameEntity GameEntity => new GameEntity(base.gameObject);
    protected new void StartCoroutine(IEnumerator c) => base.StartCoroutine(c);
    private void OnEnable() => OnEnableGameEntity();      // 靠 Unity 回调驱动生命周期
}
```

**目标形状**：`GameEntityScript` 变成**普通类**，只持有 `IGameEntity` + `ICoroutineRunner` + 生命周期虚方法；
Unity 侧用适配器把它挂到 `MonoBehaviour` 上。

```
Engine.Abstraction:
    abstract class GameEntityScript            // 纯 C#，无 MonoBehaviour
        IGameEntity GameEntity { get; }
        ITransform Transform => GameEntity.Transform;
        ICoroutineRunner Coroutines { get; }
        virtual void OnEnableGameEntity() / OnDisableGameEntity() / OnUpdateGameEntity(float)

Engine.Unity:
    sealed class UnityMonoBehaviourHost : MonoBehaviour   // 一个适配器 + 反射派发
        // OnEnable/OnDisable/Update/CollisionEnter → 调 GameEntityScript 的虚方法
```

- **上游改动**：1 个文件重写（约 60 行）+ 新增 1 个适配器（约 40 行）。
- **游戏逻辑改动**：0 行（所有派生类不受影响，`Transform`/`GameEntity`/`StartCoroutine` 名字不变）。
- **收益**：219 个文件里的绝大多数从此不认识 Unity；移植侧那套 `GameEntityScriptRuntime` 反射派发可以直接复用（甚至变得更干净，可以转成接口派发）。
- **风险**：低。Unity 侧行为等价（适配器照原样转调）。

> 附带价值：这一步同时让 **AOT/IL2CPP/CoreCLR-AOT 友好**——生命周期不再依赖 Unity 的反射式消息派发，也不再依赖 `MonoBehaviour` 的 IL2CPP 白名单。

### L2 ⭐ 协程（62 处 / 25 文件）

现状：`StartCoroutine` 走 `MonoBehaviour`，`yield return` 有 228 处。
好消息：`Engine` 里已有 `CoroutineYieldInstruction`，移植侧已有完整调度器（支持 null/float/Task/嵌套 IEnumerator/自定义 yield）。

**微调**：
1. `GameEntityScript.StartCoroutine` 改为走 `ICoroutineRunner`（L1 已顺带解决）；
2. 所有 `yield return new WaitForSeconds(...)` 换成 `Engine.Coroutine` 里的等价类型（一次性机械替换，`WaitForSeconds` 的语义两套后端都能实现）；
3. **禁止**在 `Pal3.Game` 里出现 `MonoBehaviour` 类型的 `StartCoroutine`。

- 上游改动：机械替换 + L1 的调度器注入；逻辑 0 行。
- 验收：`grep -r "StartCoroutine" Pal3.Game` 全部命中 `GameEntityScript` 基类实现。

### L3 UI / uGUI / TMPro（16 文件，约 167 处组件引用）

涉及：`Pal3.cs`(39 个 SerializeField)、`CameraManager`、`MainMenu`、`DialogueManager`/`DialogueTextProcessor`、`MinimapManager`、`WorldMapManager`、`TaskManager`、`TouchControlUIManager`、`InformationManager`、`VideoManager`。

这是**唯一需要动游戏代码结构**的地方，但办法很省：**视图边界**。

```
现状:  DialogueManager 直接持有 Image/TextMeshProUGUI 字段并更新它们
目标:  DialogueManager 只调用 IDialogueView.SetSpeaker(...) / SetText(...)
       后端: UnityDialogueView (uGUI)   |   UeDialogueView (UMG Slate)
```

- 只需为**每个界面抽一个 View 接口**（约 8 个接口），每个接口 5~15 个方法；管理器内部把 `image.sprite = x` 换成 `_view.SetAvatar(x)`。
- 上游改动：8 个新接口 + 8 个 Unity View 实现（**新增**，不删原代码，原逻辑搬到 View 里）+ 管理器里约 150 行调用改写。
- 收益：`Pal3.Game` 不再引用 uGUI/TMPro；移植侧 ~500 行 uGUI 桩直接删掉，且**UI 真的能显示**（当前所有 UI 都落在 AutoFill 出来的隐形对象上）。
- 顺序建议：对话 → 主菜单 → 小地图/大地图 → 触摸控制。触摸控制优先级最低（PC/主机端可后置）。

### L4 渲染抽象（38 Material / 35 Mesh / 42 Renderer / 5 Texture2D / 10 RenderSettings / 7 Light）

现状：`Engine` 已有 `ITexture2D`/`IMaterial`/`ISprite` 抽象，但 `Pal3.Game` 里仍有直接 `new Material(...)`、`new Texture2D(...)`、`Mesh` 拼装、`RenderSettings.fog/skybox`、`Light` 设置。

**微调**（按三组）：
1. **材质/纹理**：把 Game 层里剩的直接构造，改为走已有的 `IMaterialFactory`/`ITextureFactory`（契约在，补几个工厂方法：`CreateFromRgba`、`CreateUnlit`、`SetTexture`…）。
2. **网格**：`Mesh` 拼装（导航网格、Skybox、静态 billboard）抽成 `IMeshBuilder`/`IMesh`（顶点/索引/UV 数组 + 提交）；Unity 后端 `new Mesh`，UE 后端走 ProceduralMesh/`UProceduralMeshComponent`。
3. **环境**：`RenderSettings`/`Light` 抽成 `IEnvironmentSettings`（`SetFog`/`SetAmbient`/`SetSun`/`SetSkybox`），数据源本来就是 `LgtFile`（已移植且是真的），只是"应用"这一步要进后端。

- 上游改动：约 15 个文件，多为"把直接构造换成工厂调用"。
- 收益：移植侧白纹理/空 Mesh/无雾无光全部消失，**场景第一次真正可见**。

### L5 相机（26 文件触及，核心 1 个）

现状：`Pal3.cs` 里 `mainCamera` 是 SerializeField；`Camera.main` 在 shim 里永远是 null；`CameraManager`（956 行）逻辑全是真的，但作用在一个假相机上。

**微调**：`ICameraService`（`Vector3 WorldToScreenPoint`、`Ray ScreenPointToRay`、`SetFov/LookAt`、`TryGetMainCameraEntity`），
`CameraManager` 只依赖它；Unity 后端包 `Camera`，UE 后端包 `PlayerCameraManager.ViewTarget`。

- 上游改动：1 个接口 + `CameraManager` 里约 20 处调用点改写。
- 收益：运镜/跟随/战斗镜头在 UE 里第一次生效（`UnityUeConvert` 已经把坐标缝做好了）。

### L6 输入（17 文件，`InputAction` 360 处 / `InputSystem` 258 处）

现状：生成式 `PlayerInputActions`（约 2600 行）直接绑 Unity InputSystem，`InputManager` 从它读值。

**微调**：把"输入"变成**数据 + 快照**，而不是 Unity 对象：

```
IPlayerInput
    bool  GetButton(InputButton id)        // 交互/确认/取消/跑/跳…
    Vector2 GetMoveAxis() / GetLookAxis()
    event Action<InputButton> Pressed
```

- Unity 后端：`PlayerInputActions` 仍在，但只作为 `IPlayerInput` 的实现内部细节（**生成代码不用改**）。
- UE 后端：EnhancedInput 的 mapping context → 同一接口。
- 上游改动：`InputManager` 构造函数换注入（约 30 行），其余 17 个消费者无感。

### L7 物理 / 碰撞（35 文件触及 Collider/Bounds）

现状：`IPhysicsManager` 已抽象（射线/BoxCast），但 `entity.GetComponent<Collider>()`、`Collider.bounds` 是 Unity 类型。

**微调**：把"碰撞体"降级为**实体上的数据**：`IGameEntity.Bounds`（`Bounds` 是纯结构体，已在 Engine 里）+
`IColliderQuery`（`bool TryGetBounds(IGameEntity, out Bounds)`）。Unity 后端从 `Collider.bounds` 取；UE 后端从 `FBoxSphereBounds` 取（或从场景数据直接算，PAL3 的碰撞本来就是数据驱动的盒子）。

- 上游改动：约 10 个文件（`ActorMovementController` 为主）。
- 收益：移植侧"物理全 miss"的静默行为缺失消失；平台/斜坡/推箱逻辑可验证。

### L8 音频 / 视频 / 时间 / 日志 / 平台 / 持久化

| 项 | 现状 | 微调 |
|---|---|---|
| 音频（7 文件，`AudioSource` 10 处） | `AudioManager` 逻辑真，`AudioSource` 是 Unity 组件挂在实体上 | `IAudioSource`（Play/Stop/Loop/Volume/3D 位置）+ 实体获取；UE 后端 `UAudioComponent` |
| 视频（2 文件） | `VideoPlayer.loopPointReached` 推进过场状态机 | `IVideoPlayer.Play(url)` + **显式完成回调/awaiter**（不要靠事件），上游 `VideoManager` 里 2 处等待改写 |
| 时间/日志/平台 | 已抽象（`IGameTimeProvider`、`EngineLogger`），`Time.` 仅 2 处、`Debug.Log` 仅 3 处 | 顺手清掉即可 |
| 持久化 | `PlayerPrefs` 仅在启动路径 | `ISettingsStore`（已有 `PlayerPrefsStore` 雏形） |
| 编码 | `Encoding.GetEncoding(936)`（GBK）9 处 | 保持现状（启动时 `RegisterProvider`），或抽 `ITextEncodingProvider`；**迁移 .NET 时注意 icu/app-local 差异** |

### L9 序列化字段 / 预制体（结构性，最大单点）

现状：`Pal3.cs` 有 **39 个 `[SerializeField]`**（所有 UI 引用、相机、预制体），这是 Unity 编辑器的场景装配信息。非 Unity 宿主没有 inspector，移植侧只能 `AutoFillStubFields` 反射 new 出假对象。

**微调**：把这 39 个字段变成**一份配置对象**：

```
GameViewConfig {  // 由后端提供：Unity 里从 inspector 灌入；UE 里从 UMG 蓝图/DataAsset 灌入
    IDialogueView Dialogue; IMinimapView Minimap; ... ICameraService Camera; ...
}
Pal3Game(GameViewConfig config)   // 构造注入，替代 39 个字段
```

- 上游改动：`Pal3.cs` 约 40 行字段声明 → 1 个配置类 + 1 个 Unity 装配器（把 inspector 的 39 个对象塞进配置）。
- 收益：移植侧那 39 个 AutoFill 假对象和 `InstantiateHook` 反射把戏全部消失；也解决了"预制体只有 Unity 有"的问题（预制体 = View 工厂 + 配置）。

### L10 生命周期调度（AOT 相关，顺带收益）

移植侧目前用**反射**抓 `Awake/Start/Update/LateUpdate`。Mono 下没问题，但 CoreCLR-AOT / IL2CPP 下反射+`MethodInfo.Invoke` 会踩裁剪与性能问题。

**微调**：L1 的普通类基类里定义**虚方法**（不是反射找方法名），派生类 override；调度直接虚调用。

- 上游改动：`GameEntityScript` 基类（L1 同一处）+ 各脚本把 `private void Update()` 改 `protected override void OnUpdateGameEntity`。
- 代价：约 60 个脚本各改 1~2 行（机械）。
- 注意：上游目前大量 `Update()` 是 private —— 若嫌改动大，可保留反射作过渡，但 AOT 前必须完成。

---

## 3. 三阶段路线（建议按此排期）

### Tier 0 —— 机械分层（1~2 天，零逻辑改动）
1. `Engine` 拆成 `Engine.Abstraction` + `Engine.Unity`；
2. `GameEntityScript` 去 MonoBehaviour 化 + 适配器；
3. 协程 yield 类型统一；
4. 加"可移植性 lint"（CI 里 grep：`Pal3.Game`/`Pal3.Core` 不得出现 `UnityEngine`）。

**验收**：`Pal3.Game` 编译时不再引用 UnityEngine 程序集？——不会立刻达成（还有 46 个文件直接 using），但**新代码**被约束，边界确立。

### Tier 1 —— 服务收口（3~5 天，改动集中，收益立竿见影）
5. `ICameraService`（L5）
6. `IEnvironmentSettings` + `IMeshBuilder` + 材质/纹理工厂补齐（L4）
7. `IPlayerInput`（L6）
8. `IColliderQuery` / `Bounds`（L7）
9. `IAudioSource` / `IVideoPlayer`（L8）

**验收**：`grep -r "using UnityEngine" Pal3.Game` 降到 ≤ 10 个文件（剩下的都是 UI）。

### Tier 2 —— UI 视图化 + 配置注入（1~2 周，动结构）
10. View 接口 + Unity View 实现（L3）
11. `GameViewConfig`（L9）
12. 反射生命周期 → 虚方法（L10，AOT 前必须）

**验收**：
```
grep -r "UnityEngine" Pal3.Core Pal3.Game --include=*.cs  →  0
```
移植侧 `UnityCompatStubs.cs` 从 1214 行降到 < 200 行（只剩第三方库桩：IngameDebugConsole、SimpleFileBrowser），
`AutoFillStubFields` 与 `InstantiateHook` 反射可删。

---

## 4. 不建议的做法

| 反模式 | 原因 |
|---|---|
| 把移植侧的 `UnityEngine` 垫片当长期方案 | 垫片只能"编译通过"，永远无法"行为正确"；每加一个 Unity API 就是一次手写仿真。它只适合作为**移植过渡期**的脚手架。 |
| 重写 `Pal3.Game` 逻辑 | 219 个文件、上游还在演进（可 merge），重写=放弃上游合并能力。 |
| 把 Unity 依赖用 `#if UNITY_XXX` 包起来 | 同一份代码里两套实现，维护成本翻倍；正确做法是接口 + 后端程序集。 |
| 一步到位大 DI 框架 | 上游已有 `ServiceLocator`，够用；引入容器只会增加理解成本。 |
| 先做性能（含 C++ 化） | 行为还没跑对，性能基准无意义（见 §5）。 |

---

## 5. 与 C++ 文件格式加载计划的关系

`Pal3.Core` **零 Unity 依赖**，所以 C++ 化是最安全的切口，且**不受本文任何改动阻塞**：

```
Pal3.Core 读文件：File.ReadAllBytes → ICpkFileSystem / IMv3Model / ICvdModel
                                   ↑
                     这一层替换成 C++ 实现（UFUNCTION 暴露），
                     其余解码/命令逻辑保持 C# 不动或后续再迁
```

建议顺序：**先让托管侧行为跑对（本文 Tier 1 完成）→ 建立逐格式对拍基准 → 再换 C++ 实现**。
理由：C++ 版需要一份可信的参考输出；反过来做会把"逻辑错误"和"移植错误"混在一起查。

另外注意：C++ 化后 `Pal3.Core` 若出现 UE 依赖，会破坏"Core 纯数据层"的定位——接口定义留在 C#（`ICpkFileSystem`），C++ 实现通过 UFUNCTION 注入，保持方向依赖。

---

## 6. 工作量估算（按上游改动量）

| 阶段 | 上游文件改动 | 新增代码 | 预计 |
|---|---|---|---|
| Tier 0 | 3 | ~120 行 | 1~2 天 |
| Tier 1 | ~55（多为工厂调用替换） | ~600 行接口/后端 | 3~5 天 |
| Tier 2 | ~20（UI 管理器）+ Pal3.cs | ~1200 行（接口+View） | 1~2 周 |
| 合计 | 约 80 文件、**改动 <2000 行** | 约 2000 行 | 2~3 周 |

对比：现有移植侧为"垫片 + 桩"付出约 **3350 行**手写仿真代码，且行为永远不对。同一份投入改成上游接口化后，**两套后端都受益**（Unity 侧反而更干净：`GameEntityScript` 不再是 MonoBehaviour，测试也更容易）。

---

## 7. 移植侧（SharpDemo）的对应清理清单

完成上述改造后，`Script/` 下可删除/收缩：

| 现状文件 | 行数 | 处理 |
|---|---|---|
| `UnityEngine/UnityTypes.cs` | 433 | 只保留数学与 `Time/Debug/Application`；场景对象类型删除 |
| `UnityEngine/` 其余 13 文件 | 744 | 数学类保留（纯结构体），其余随类型迁移删除 |
| `Pal3.Game/Stubs/UnityCompatStubs.cs` | 1214 | uGUI/TMPro/InputSystem/Video/音频/AudioClip/Renderer 全部删除，仅留第三方库桩 |
| `Pal3.Game/Stubs/GameEntityFactory.cs` | 318 | 保留 `PhysicsManager` 直到 L7 完成；其余删除 |
| `Pal3.Game/Stubs/GameEntityScriptRuntime.cs` | 471 | 保留（真实现），转为接口派发后更短 |
| `Pal3.Game/Stubs/Pal3Boot.cs` | 169 | `AutoFillStubFields`/`InstantiateHook` 反射段删除，改为 `GameViewConfig` 装配 |

预期最终 `Script/Stubs` 总量 < 400 行，且**全部是真实行为**，没有"编译可行、运行无效"的假实现。

## 后续评估 · MonoBehaviour 兼容

- TODO：在现有 `APal3Driver.Tick → GameEntityScriptRuntime` 上评估轻量 MonoBehaviour 兼容层。先挑一个真实的上游玩法组件验证挂载、`Awake`、首次 `Start`、禁用与重启、销毁和协程清理。生命周期仍由同一个帧入口驱动。
