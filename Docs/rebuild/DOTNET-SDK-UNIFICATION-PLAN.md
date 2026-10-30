# DotNetRuntimeSDK 统一 + iOS CoreCLR(dotnet11) 计划

> 状态：**计划稿，待讨论**（2026-10-28 起）
> 本文只记录结论与待办，不记录探索过程；过程见 `android-runtime-matrix-mac`、`CLR-REMOTE-DEBUG.md`。

## 0. 一句话结论

把 `MonoSDK` 与 `CoreClrSDK` 的**插件侧代码**（Build.cs / staging / 版本表）收成一份 `DotNetRuntimeSDK`，二进制仓库暂不合并；iOS CoreCLR 只走 dotnet 11（10 的 iOS CoreCLR 明确放弃）。顺序：先做一个便宜的兼容性判定实验，再统一，最后开 11 分支。

## 1. 已确定的决策

### 1.1 版本矩阵不是全局常量

**2026-10-29 定稿：运行时分支跟 backend 走，不跟平台走。**

| backend × platform | runtime | 依据 |
| --- | --- | --- |
| Mono × 全部平台 | **10**（`release/10.0`） | .NET 11 的 coreclr 树里没有 mono，`mono+libs` 无法编译；Mono 只能停在 10 |
| CoreCLR × iOS | **11**（`release/11.0`） | 10 的 iOS CoreCLR 默认不开解释器，iOS 无 JIT 等于没有引擎；自建要持续改 SDK 内部 |
| CoreCLR × Android | **11** | 2026-10-30 用留档的 11 载荷验证通过（B/D 组合），`*_ANDROID` 例外已删除 |

托管程序集 TFM 统一 `net10.0`：11 运行时可以承载 net10 IL（IOSClrDemo 8/8 实测），
所以「升 runtime」不等于「升 TFM」。

建模方式（已落地）：`CoreClrSDK/DotNetRuntime.version` 描述 CLR 载荷的 (major, tfm, version)，
默认值即 CoreCLR 的 11；Android 例外用 `<KEY>_ANDROID` 三行显式表达，删掉即完成迁移。

- `CopySDKFromSrc.sh` 按 `<KEY>_<PLATFORM>` → `<KEY>` 解析，并校验 `$DOTNET_SRC/eng/Versions.props`
  的 `<MajorVersion>` 与解析结果一致（防止把 10 的产物拷进 iOS 槽）；
- `CoreClrSDK.Build.cs` 解析同一份文件，与载荷 `VERSION.txt` 的 `Runtime: <major>.<ver> (<tfm>)`
  比对，不一致抛 `BuildException`；
- Mono 侧的分支/TFM 固定在 `MonoSDK/BuildMonoSDK.sh`（`net10.0-<os>-<type>-<arch>`）。

逐处 diff 的依据见 `Docs/rebuild/CORECLR-SDK-REVIEW.md`。

### 1.2 iOS CoreCLR 放弃 dotnet 10

iOS 侧目标形态是 **AOT BCL + 解释器**。dotnet 10 的 iOS CoreCLR 默认不带解释器，自建要改 SDK 内部开关，成本与风险都不可控。10 上实测的失败点（仅作记录，不再追）：

```
dyld: Library not loaded: @rpath/libclrinterpreter.dylib
  Referenced from: .../SharpDemo.app/Frameworks/CoreCLR.framework/CoreCLR
```

原因已定位：`CoreCLR.framework/CoreCLR` 的 `LC_RPATH` 只有构建机绝对路径（`.../ios.arm64.Release/interpreter`），而 `libclrinterpreter.dylib` 的 install name 是 `@rpath/CoreCLR.framework/libclrinterpreter.dylib`。`MakeCoreClrFramework.sh` 目前只改 `install_name`，不写 `LC_RPATH`（11 的官方产物自带 `@loader_path`，所以历史上没暴露）。**该问题随「iOS CoreCLR 只用 11」的绑定一起作废**，`MakeCoreClrFramework.sh` 不再需要处理 rpath。

### 1.3 二进制仓库合并（2026-10-31 修订：合并，见 §2.5）

~~暂不合并~~。原论据（载荷数百 MB 拖慢 CI/下载）在「载荷 git-ignored + 按平台/后端取用」模型下不成立：仓库本身只有脚本与版本文件，Release 按平台出资产，消费端只下载需要的一两份。合并后反而修掉现有不对称——Mono 载荷 git-ignored 且无 fetch 脚本（换机全靠手工搬），只有 CLR 有 Releases 拉取。

### 1.4 calli ABI：必须双胞胎，不能改解释器（2026-10-28 静态评估）

`generated calli` 的签名约束在 10 和 11 上**逐字相同**，静态证据：

- `src/coreclr/vm/dllimport.cpp` `PInvoke::MarshalingRequired`：`ELEMENT_TYPE_BYREF` 没有 case，落到 default 的"非基元 → TRUE"；`BOOLEAN/CHAR` 在 runtime marshalling 开启时 TRUE；`STRING` 非基元 → TRUE。10 与 11 的该函数完全一致。
- `src/coreclr/interpreter/compiler.cpp`：`if (pInvokeMarshalingRequired(...)) BADCODE("PInvoke marshalling for calli is not supported in interpreted code")` —— 11 仍然保留。

三种方案对比（结论：选 A）：

| 方案 | 改动面 | string 安全 | 升级成本 |
| --- | --- | --- | --- |
| **A. 生成器发 ABI 双胞胎**（`string→char*`、`ref/out→T*`、`bool→byte`） | 单文件 ~140 行，`needsAbiTwin` 门控 | ✅ | 无（与 runtime 版本无关） |
| B. 跳过解释器的 calli 封送检查（1 行） | 1 行 | ❌ `string` 会把 System.String 对象引用直接交给原生（按 `const char*` 读对象头）→ 静默内存破坏 | 每次升 SDK 重打补丁 |
| C. 在解释器实现 calli 封送 stub | 编译器 + 运行时大改 | ✅ | 最高 |

已落地（`dotnet11-ios` 分支，工作区）：双胞胎生成器 + `CSGlueGenerator` 的 commandlet 跳过（顺带修掉 glue 丢 Niagara tags）+ `Tools/verify-calli-abi.py` 静态门禁（扫描生成的 calli **调用点**，非类型转换；已用注入回归做过负向自检）。

## 2. 统一方案（B 阶段）结构

```
<PluginRoot>/                          # UnrealSharp 插件根
├── Source/ThirdParty/DotNetRuntimeSDK/
│   ├── DotNetRuntimeSDK.Build.cs     # 唯一 External 模块：resolver 调用 + staging + 链接 + framework
│   ├── RuntimeBackend.cs             # UnrealSharpRuntimeSelection（从 MonoSDK.Build.cs 迁出；含 ExternalDependencies）
│   ├── DotNetRuntime.version         # v2：backend 前缀键（MONO_*/CORECLR_*），CI 与本地构建共同消费
│   ├── FetchRuntimeSDK.sh            # GitHub-Releases 单入口（backend+platform+arch）
│   ├── MonoSDK_APL.xml               # 原样迁入（每个 backend 一份）
│   └── CoreClrSDK_APL.xml
└── Vendor/                           # 载荷 submodule（Source/ 之外，UBT 不扫描）
    ├── MonoSDK/<Platform>/<arch>/    # 载荷 + BuildMonoSDK.sh + CI（仓库不动，§1.3）
    └── CoreClrSDK/<Platform>/<arch>/ # 载荷 + BuildCoreClrSDK.sh/CopySDKFromSrc.sh + CI
```

要点：

1. **resolver 独立**。当前 `UnrealSharpRuntimeSelection` 寄生在 `MonoSDK.Build.cs`，`CoreClrSDK.Build.cs` 反向依赖它——这是设计异味，也是「改一处漏一处」的温床。
2. **staging 只有一份实现**。BCL → `Content/Managed/<Platform>`、项目 DLL/PDB/JSON、APL/receipt 挂接，全走同一组函数。今天两个 `Build.cs` 各写一遍路径拼装，直接用出了事故：`CoreClrSDK.Build.cs` 在 layout 改成 `<Platform>/<arch>/` 后仍引用 `Android/lib`，导致 `libcoreclr.so` 从未进入链接行（`e6b3be0` 修复）。
3. **过渡期用薄壳**。新模块先接管 resolver + staging，`MonoSDK` / `CoreClrSDK` 退化为只声明库路径的 shim；六组合回归通过后再删壳。
4. **iOS CoreCLR 在 C 落地前不产出载荷**，避免一份跑不起来的 SDK 被误用。

### 2.5 合并可行性论证（2026-10-31）

**结论：可行，收益明确，风险有界。预估 2~2.5 天，用薄壳过渡 + 六组合回归做门禁。**

#### 范围：合什么、不合什么

| 项 | 处置 | 依据 |
| --- | --- | --- |
| `MonoSDK.Build.cs`(764 行) + `CoreClrSDK.Build.cs`(431 行) | **合并**为 `DotNetRuntimeSDK.Build.cs` 单入口 | 1195 行里装配逻辑（staging/链接/framework/APL 挂接）两份同构 |
| `UnrealSharpRuntimeSelection`（寄生在 MonoSDK） | **迁出**为独立 `RuntimeBackend.cs` | CoreClrSDK 反向引用它，依赖方向倒挂 |
| `DotNetRuntime.version` | **升级 v2**：backend 前缀键（`MONO_SOURCE_TAG` / `CORECLR_SOURCE_TAG` / 各 TFM / `BUILD_SUBSET_MONO`…） | 文本文件 CI 可直接 grep（workflow 已在这么用）；Mono 侧今天没有版本文件（TFM 硬编码在 BuildMonoSDK.sh、tag 在 CI env）——正好收编 |
| 签名三件套 + 缓存（~200 行 × 2 份） | **去重**成一份 | 两份逐行相同，本次对齐双因子就是手工复制 |
| `FetchCoreClrSDK.sh` / fetch 入口 | 统一为一个按 backend+platform 分派 | GitHub-Releases-only 策略两边一致 |
| 两个 APL（`MonoSDK_APL.xml` / `CoreClrSDK_APL.xml`） | **保留两份**，随模块迁址 | 内容本质不同（libmonosgen-2.0.so vs libcoreclr.so），backend gate 已保证互斥 |
| 运行时宿主（`CSDotNetRuntimeHost_Mono/Mobile/Desktop`） | **不合** | mono embedding API vs 裸 coreclr/hostfxr 是本质不同的实现层，属"运行时适配"不属"SDK 装配" |
| 两个载荷仓库（RPG3D/MonoSDK、RPG3D/CoreClrSDK）及其 CI | **不动**（§1.3 既定） | CI 刚完成 tag 钉定（v10.0.14 / rc.3），别一次动两个系统；Build*SDK.sh 是 CI 财产留在原仓库 |

#### 证据一：职责重叠清单（合并 = 纯去重）

- 签名：`SignEmbeddedFrameworkZip` / `FindCodeSigningIdentity` / `GetConfiguredSigningTeam` / 缓存四件，两份逐行等价。
- BCL staging：Mono 侧 4 个平台分支（Win64/Mac/Linux/Android/IOS）各写一遍「枚举 runtime/*.dll → `Content/Managed/<Platform>` UFS」，CoreClr 侧一份同样的循环；目的地、PDB/JSON 处理完全同构。
- 宏：`UNREALSHARP_MONO=1` 在 MonoSDK 的每个平台分支里重复 `PublicDefinitions.Add`（5 处），下游 13 个文件消费——合并后一处定义。
- 依赖：`UnrealSharpCore.Build.cs` 同时依赖两个 SDK 模块只为拿 include 与宏；合并后单一依赖。

#### 证据二：最近事故全部是"同一改动、两处落地"漏一处

1. `e6b3be0`：CoreClrSDK 载荷改 `<arch>` 层，Build.cs 没跟上 → `libcoreclr.so` 从未进链接行，B/D 组合链接期才炸。
2. `8a78366`/`dbe5d3b`：mono BCL 的 ios/iossimulator 目录拆分要改两处路径逻辑。
3. 本轮：`build-coreclr-sdk.yml` 的 build 子集 vs 版本文件 `BUILD_SUBSET` 漂移（`clr.tools+clr.packages` 纯浪费且掩盖了子集变更）；三个 workflow 的 runtime ref 各自漂（v10.0.12 / rc.1 / rc.1 对 11.0.0）。
4. 本轮：iossimulator 补丁门只开 `ios`（设备补了、模拟器漏了）→ CI 红。
5. 双因子签名缓存升级 = 手工把 MonoSDK 的实现复制到 CoreClrSDK。

每一条在单一模块里都不可能发生。

#### 证据三：本轮工作已经把最难的耦合拆掉了

- 托管层统一（无双 backend 宏的托管 DLL 集），双胞胎生成器与 backend 无关；
- CoreLib/probe 契约两平台同构（同一条规则、同一份代码路径）；
- 签名机制两边逐行等价（刚对齐完）；
- 版本钉定模式在 CoreClrSDK 成型（SOURCE_TAG + 守卫 + stamp），Mono 纳入是机械活；
- 打包矩阵脚本（`build-android/ios-mac.sh`）由 ini 驱动、与模块内部结构解耦。

#### 结构决策（2026-10-31 用户拍板）：新建单一子仓库，Mono+CLR 载荷与构建同仓

用户方案：**新建一个子仓库（submodule）**，同时容纳 Mono 与 CoreCLR 的载荷目录、构建脚本、版本文件与 `Build.cs`；`Build.cs` 内部按 (backend, platform) 条件选择 lib/头文件；GH Action **10 个平台 leg 并发**（Mono 7：win64/linux/mac-x64/mac-arm64/android/ios/iossim + CoreCLR 3：android/ios/iossim）。

评估结论：**采纳，优于本节此前的 Vendor 双 submodule 方案**。理由：

1. **天然解决 UBT 扫描问题**——一个 submodule 一份 Build.cs，不存在"旧 Build.cs 仍被扫描"的尴尬，§2.5 的 Vendor 搬家与空壳两个方案都不需要了。
2. **修掉载荷分发的现有不对称**：Mono 载荷 git-ignored 且无 fetch 脚本（换机/新克隆全靠手工搬）；统一后 `FetchRuntimeSDK.sh` 按 backend+platform 从单一 Release 拉取。
3. **一次 watchtower**：10 leg 并发，wall time = 最慢 leg（≈25 min），与今天两个仓库各自跑无差；分钟账单不变（同样的 leg 集合），注意 macOS 计费 10× 是既有大头。
4. **版本原子性**：一次发布同时携带 mono@v10.0.14 + clr@rc.3，`DotNetRuntime.version` 是唯一事实源；"统一更新 tag" = 改一行 + 重跑 workflow + 切 Release。

目标形态：

```
<PluginRoot>/Source/ThirdParty/DotNetRuntimeSDK/   # 新子仓库（submodule）
├── DotNetRuntimeSDK.Build.cs      # 唯一 External 模块：条件选择 lib/头文件 + staging + framework
├── RuntimeBackend.cs              # resolver（从 MonoSDK.Build.cs 迁出；含 ExternalDependencies）
├── DotNetRuntime.version          # v2：MONO_SOURCE_TAG / CORECLR_SOURCE_TAG / *_TFM / BUILD_SUBSET_*
├── Mono/<Platform>/<arch>/        # 载荷（git-ignored；mono 7 平台）
├── CoreCLR/<Platform>/<arch>/     # 载荷（git-ignored；clr 3 移动平台；desktop=hostfxr 无载荷）
├── BuildMonoSDK.sh|.bat / MakeMonoFramework.sh / CopySDKFromSrc.mono.sh
├── BuildCoreClrSDK.sh / CopySDKFromSrc.clr.sh / MakeCoreClrFramework.sh
├── FetchRuntimeSDK.sh             # GitHub-Releases 单入口（backend+platform+arch）
├── MonoSDK_APL.xml / CoreClrSDK_APL.xml
└── .github/workflows/build-all.yml（10 leg，leg 级可单独 dispatch）+ package-release.yml
```

要点与约束：

- **Build.cs 放子仓库内**（与今天 CoreClrSDK 同构：仓库兼任"生产者脚本"与"消费者模块"），resolver 一并迁入；插件侧 `UnrealSharpCore/Utilities` 的依赖从两个模块改为一个。
- **leg 级独立 dispatch**：mono 与 clr 升级节奏不同（10.0.x servicing vs 11 GA），改 CLR 不应强制重编 Mono——workflow inputs 支持按 leg 开关（当前 build-all 已具备雏形）。
- **Release 资产按 platform 分文件**（`MonoSDK-Mac-arm64.zip`、`CoreClrSDK-IOS-arm64.zip`…），消费端按需取；不做单一巨型 zip（§1.3 原顾虑仅对此成立）。
- **迁移期产物**：旧两仓 archive 保留（脚本历史），新仓脚本以拷贝入驻（`FetchCoreClrSDK.sh` 的 REPO 常量改指新仓）；插件移除旧 gitlink、挂新 submodule。
- **回归门禁不变**：六组合矩阵全绿。

风险对照（与 §2.5 风险表合并看）：新增一条——`UNREALSHARP_MONO` 宏与 include 路径的传播链要从「MonoSDK→Core→Editor」改为「DotNetRuntimeSDK→Core→Editor」，属机械改动但必须全绿验证；另 `CopySDKFromSrc.sh` 拆成 mono/clr 两份同名入仓时注意 CI 调用路径。

#### 风险与对策

| 风险 | 对策 |
| --- | --- |
| UBT makefile 缓存不因 ini 变化失效 | `ExternalDependencies` 随 resolver 迁移（模式已验证，`30ca4cc`） |
| submodule 移动断 CI/本地脚本 | 脚本内部全用相对自身路径（现状即是）；模块侧路径收敛为常量 |
| 中间态两套并存太久 | 薄壳与删除放同一条 PR 链，不留过夜中间态 |
| Mono 载荷无版本戳，守卫没法像 CLR 一样核对 | 守卫按 backend 分派：CLR 维持 major 校验；Mono 先只查目录存在（BuildMonoSDK 无 stamp），v2 版本文件加 `MONO_PAYLOAD_MIN_FILES` 之类的存在性门 |
| 回归成本 | 六组合矩阵脚本就绪，全绿即门禁 |

#### 工作量与顺序

1. `RuntimeBackend.cs` 抽取 + `ExternalDependencies`（0.5h）
2. `DotNetRuntimeSDK.Build.cs` 骨架 + platform×backend 派发（0.5 天；先接 Mono 全平台，量最大）
3. CoreCLR 接入（mobile link/framework/APL；desktop = hostfxr no-op）（0.5 天）
4. 签名去重 + 版本文件 v2 + Mono 载荷存在性门（0.5 天）
5. submodule 移位（甲/乙）+ 旧 Build.cs 退役（0.5 天）
6. 六组合回归 + 文档（0.5~1 天）
7. 新子仓库组装（脚本拷贝/更名、workflow 合并 10 leg、Release 政策）+ 插件换 gitlink（0.5 天）——用户拍板后并入


#### 对"先不动"的反驳

稳定窗口恰是重构窗口：回归矩阵已脚本化、每个组合都有真机基线，此时合并的验证成本最低；反之每加一个平台/后端，维护面按 2× 增长，而漂移事故（证据二）全部发生在"双份维护"期间。B 阶段的产出物就是这个合并本身——不存在"等 B 之后再合"的顺序。

## 3. 阶段与判定标准

| 阶段 | 内容 | 判定标准 | 预估 |
| --- | --- | --- | --- |
| **A** | `CoreClrDemo/IOSClrDemo`：runtime 11 × managed **net10** vs runtime 11 × managed **net11** | 逐项过：`coreclr_initialize` → 托管入口 → P/Invoke（System.Native）→ **泛型/反射实例化**（解释器 + AOT BCL 下最易缺）→ 线程/异常。第一格通过 = Sharp 可留 net10；不通过 = C 范围翻倍（托管层也要升 11） | 0.5 天 |
| **B** | 统一 SDK（10 基线，iOS CLR 不产出） | Android A/B/C/D 四组合 + iOS 两组（编辑器 Mono/CoreCLR × iOS Mono）全部真机通过 | 1~1.5 天 |
| **C** | 开 11 分支：iOS CoreCLR（换 11 载荷 + framework rpath/install_name 处理） | iOS 真机启动 + 托管入口 + 玩法可跑 | 2~3 天 |

**为什么 A 先行**：A 的结论决定 C 是「只换载荷」还是「托管层整体升级」，两者工作量差数倍；而 A 不需要 UE 打包，成本极低。
**为什么 B 在 C 之前**：C 要改的正是 SDK 脚本与打包逻辑；在两份重复代码上并行修改，等于把今天的 arch 路径事故复制一遍。

## 4. 当前基础设施状态（2026-10-28）

### 4.0 C 阶段进展（本地，未提交）

分支 `dotnet11-ios`（从 `mobileclr-unify` 拉出）。已完成：

1. **net11 CoreCLR iOS 载荷本地构建成功**（不依赖 CI）：`release/11.0`@`2e6f9076461`，worktree `~/Documents/Code/DotNet11`，产物 `CoreClrSDK/IOS/arm64/`（181 BCL + natives + `CoreCLR.embeddedframework.zip`），`RuntimeProductVersionString: 11.0.0-dev`。构建前置：ninja 在 PATH（`~/Library/Python/3.9/bin`）、`PKG_CONFIG_PATH=~/.local/lib/pkgconfig`（openssl shim）、brew shim 指向 MacPorts ICU。
2. **11 的框架结构比 10 简单**：`libcoreclr.dylib` 无 `LC_RPATH`、无对 `libclrinterpreter.dylib` 的 `LC_LOAD_DYLIB`——引擎由 CoreCLR 按自身镜像目录 dlopen，因此 10 上那个 `@rpath/libclrinterpreter.dylib` 死结在 11 不出现。`MakeCoreClrFramework.sh` 无需额外 rpath 处理。
3. **iOS 打包链路跑通**（未上真机）：`build-ios-mac.sh Mono CoreCLR` → `Packaged/IOS-Mono-CoreCLR/SharpDemo.app`，bundle 内仅 `CoreCLR.framework`（引擎紧邻框架二进制、install name 已重写），主二进制链接 `@rpath/CoreCLR.framework/CoreCLR`，包内 195 个托管 DLL。
4. **Mac 桌面双后端验证通过**（双胞胎生成器 + glue 守卫）：Mono 后端与 CoreCLR（hostfxr，JIT）各跑 LV_OpenWorld，`AScriptGameMode` 正常、Shinobi 动画播放、无 `InvalidProgramException` / 托管异常。
5. **静态门禁** `Plugins/UnrealSharp/Tools/verify-calli-abi.py`：自建 emit + 扫描调用点；负向自检（人为把调用改回原始 `string` 签名）能准确拦截。

**2026-10-29 更新：iOS CoreCLR 真机跑通**

6. **IOSClrDemo 先证兼容性**：`MANAGED_TFM=net10.0`（托管 net10）+ `CoreClrSDK/IOS/arm64`（运行时 11）在 iPad 上 8/8 通过——未双胞胎的 `string`/`ref`/`bool` calli 如期抛 `InvalidProgramException`，双胞胎版本全部正确。结论：11 的运行时可以承载 net10 托管程序集，之前的失败不是版本兼容问题，而是宿主接线问题。
7. **根因：内嵌框架里的嵌套 dylib 没签名**。UBT 只签 app 与 `CoreCLR.framework` 本身，`CoreCLR.framework/Frameworks/*.dylib`、`libclrjit.dylib`、`libclrinterpreter.dylib` 全是 `code object is not signed at all`。iOS 下 dlopen 未签名 dylib 返回 EPERM，第一个 P/Invoke（`GlobalizationMode` 初始化读环境变量 → `libSystem.Native`）抛 `DllNotFoundException`，而构造这条异常又需要 SR/Globalization，递归到 `Environment.FailFast` —— 这就是"看不到托管堆栈直接 signal 6"的来源。Mono 侧早就有 `SignEmbeddedFrameworkZip`，`CoreClrSDK.Build.cs` 从没移植。
8. **修复**：把该机制移植进 `CoreClrSDK.Build.cs`（unzip → 逐个 codesign → 原生 `zip -X -y` 回包，签完再让 UE 封框架）。跳过缓存改成双因子 `<zip sha256> <identity sha1>`，证书轮换/换团队会重签；身份优先取 `Config/IOS/IOSEngine.ini` 的 `CodeSigningTeam`。MonoSDK 同步升级为双因子。
9. **顺带清掉追错根因的产物**：删除 iOS 宿主的 `HOST_RUNTIME_CONTRACT`（`pinvoke_override`/`external_assembly_probe`）。两者都是为绕"库找不到"而加，实际根因是签名；`NATIVE_DLL_SEARCH_DIRECTORIES` + 框架内 CoreLib 副本（`MakeCoreClrFramework.sh` 已预置）即可，与 IOSClrDemo 的最小配置一致。同时删掉 raw host 根本不消费的属性键（`coreclr_initialize` 只认 TPA / NATIVE_DLL_SEARCH_DIRECTORIES / APP_PATHS / PLATFORM_RESOURCE_ROOTS / bundled probe / PINVOKE_OVERRIDE / HOST_RUNTIME_CONTRACT；AppContext 开关由 hostpolicy 翻译，裸宿主只能走环境变量），保留属性键值对齐的 `static_assert`。
10. **真机结果**：`Packaged/IOS-Mono-CoreCLR` 装 iPad，`coreclr_initialize => 0x0`、`ScriptGameMode_C` 加载、`LV_OpenWorld` 进图并持续出帧。

待办（后续）：

- 双胞胎需回归 Android 四组合 + iOS Mono 两组（生成器改动影响所有后端）；
- 把 `DotNetRuntime.version` 的 11 值纳入 (backend, platform) 版本表（当前工作区临时改，未提交）；
- `build-ios-mac.sh` 已加 `Binaries/IOS` + `Saved/StagedBuilds/IOS` 清理，`Mono.framework` 残留问题关闭。


### 4.1 真机验证矩阵（全部通过）

| 组合 | 平台 | 结果 |
| --- | --- | --- |
| A 编辑器 Mono × Target Mono | Android（小米 24091RPADC） | ✅ |
| B 编辑器 Mono × Target CoreCLR | Android | ✅ `coreclr_initialize => 0x0` |
| C 编辑器 CoreCLR × Target Mono | Android | ✅ |
| D 编辑器 CoreCLR × Target CoreCLR | Android | ✅ |
| 编辑器 Mono × iOS Mono | iPad air 7 | ✅ |
| 编辑器 CoreCLR × iOS Mono | iPad air 7 | ✅ |
| 编辑器 Mono × iOS CoreCLR（.NET 11 + 解释器） | iPad air 7 | ✅ 2026-10-29，`LV_OpenWorld` 进图 |
| 编辑器 Mono × Target Mono（A） | Android（小米 24091RPADC） | ✅ 2026-10-30 双胞胎回归 |
| 编辑器 Mono × Target CoreCLR（B） | Android | ✅ 2026-10-30 双胞胎回归，`coreclr_initialize => 0x0` |
| 编辑器 CoreCLR × Target Mono（C） | Android | ✅ 2026-10-30 双胞胎回归 |
| 编辑器 CoreCLR × Target CoreCLR（D） | Android | ✅ 2026-10-30 双胞胎回归 |
| 编辑器 Mono × iOS Mono（双胞胎回归） | iPad air 7 | ✅ 2026-10-30，`ScriptGameMode_C` + `LV_OpenWorld` 0.13s，无异常（Mono AOT+interp 首次跑双胞胎） |
| 编辑器 Mono × Target CoreCLR（B，**CLR 11 载荷**） | Android | ✅ 2026-10-30 23:02，`external_assembly_probe` 交付 CoreLib |
| 编辑器 CoreCLR × Target CoreCLR（D，**CLR 11 载荷**） | Android | ✅ 2026-10-30 23:09 |
| 编辑器 Mono × iOS CoreCLR（**probe-only CoreLib**，框架无 CoreLib） | iPad air 7 | ✅ 2026-10-30 23:23，`external_assembly_probe` → `0x0` → `ScriptGameMode_C` |

打包脚本：`build-android-mac.sh <A|B|C|D>`、`build-ios-mac.sh <editorRuntime> [targetRuntime]`。

### 4.2 已合入 main 的关键修复

| 仓库 | 提交 | 内容 |
| --- | --- | --- |
| CoreClrSDK | `e6b3be0` | 载荷路径补 `<arch>` 层（`Android/arm64`、`IOS/arm64`）——B/D 组合前提 |
| CoreClrSDK | `af5a066` | runtime 钉到 `v10.0.12`（与 Win11 NuGet 验证过的版本一致） |
| CoreClrSDK | `54eaeaa` | 构建脚本：iOS 补丁在 10 分支无匹配时跳过（`pipefail` 下会静默退出）；有 ninja 时钉 `-ninja`，避免 CMake generator 冲突 |
| CoreClrSDK | `9dd7d87` | `FetchCoreClrSDK.sh` GitHub Releases 单一来源（禁用 NuGet） |
| MonoSDK | `30ca4cc` | 分层 `DotNetRuntime` resolver + 把 engine ini 注册为 `ExternalDependencies` |
| UnrealSharp | `d685672d` | iOS 覆盖目录清理修复（见 4.3） |
| UnrealSharp | `a7db3c3a` | 两个 SDK gitlink 对齐各自 main |

CI：MonoSDK 全平台 **success**；CoreClrSDK Android 22m46s ✅、iOS Simulator 21m36s ✅、iOS device 仍在跑（脚本修复后的新 run `40550066856`）。

### 4.3 iOS 覆盖目录清理（已修，附背景）

`PurgeStaleOverrideDlls` 用可执行文件 mtime 判定 `Saved/Managed/<Platform>` 里的覆盖 DLL 是否过期，但走的是 `IPlatformFile`（顶层是 Pak 层，pak 外文件一律 invalid），在 iOS 上直接 return，导致**陈旧 CoreLib 永远不被清理**，最终 Mono 报：

```
Failed to load AOT module 'System.Private.CoreLib' while running in aot-only mode: doesn't match assembly.
```

修复方式与 UE 自身 `FPipelineFileCacheManager::ClearOSPipelineCache` 一致：iOS 直接 POSIX `stat()`；其余平台保持原逻辑；完全拿不到时间戳时**全部视为陈旧**（不能证明比包内新就不该赢）。已在 iPad 上用「注入 mtime=2020 的假 CoreLib」验证清理生效。

## 5. 待办（按优先级）

### 5.1 立即（今天余下，均为小改动）

1. **glue 重生成丢 Niagara tags 治理**。每次移动端打包 `PackageProjectMobile` 会重生成 `Script/SharpDemo.RuntimeGlue/*.cs`，把 `GameplayTags.cs` 里的 Niagara 系列 tag 抹掉（已发生并被手工恢复过一次）。打包工具不应改写受版本控制的源码。修法二选一：commandlet 上下文跳过重生成，或让生成器按编辑器实际加载模块集幂等输出。
2. **SDK 版本钉 tag**。MonoSDK CI 现在克隆 `release/10.0` **分支尖端**（会漂移），CoreClrSDK 钉 tag——同一 commit 在不同日子产出不同 SDK，直接影响设备结论的可归因性。统一为 tag（MonoSDK → `v10.0.13`）。
3. **清理 iOS 的 10.0 CoreCLR 载荷**（`CoreClrSDK/IOS/arm64`），或改名留档，避免 C 阶段前被误用。

### 5.2 之后

4. A 阶段实验 → 结论写入本文。
5. B 阶段统一（含六组合回归）。
6. C 阶段 11 分支。
7. 未结清的旧账：Mac 编辑器 Mono 调试崩溃（F5 被信号拦截、F6 可继续，疑 `MONO_THREADS_SUSPEND=preemptive` 信号）；`-MonoDebugSymbols` / `mono_debug_paths.json` 是否冗余；MonoSDK Debug 产物 + `.dSYM` 拷贝 + README 调试章节。

## 6. 风险

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| A 判定不通过（runtime11 跑不了 net10 程序集） | C 范围翻倍，托管层要整体升 11，且牵动 Mono 侧构建链 | A 先做；判定标准包含泛型/反射，避免"启动即通过"的假正 |
| 统一期间回归面广 | 六组合都要重验，容易停在中间态 | 薄壳过渡 + 每步跑完整矩阵再删壳 |
| iOS framework rpath 在 11 也需要手写 | C 阶段仍要动 `MakeCoreClrFramework.sh` | 已在本文记录 `LC_RPATH` 处理点 |
| 两套 SDK 版本继续漂移 | 设备结论不可复现 | 5.1 第 2 项 |

## 7. 参考

- `Docs/rebuild/CLR-REMOTE-DEBUG.md`、`Docs/rebuild/STAGE1-CORECLR-PORT.md`
- `build-android-mac.sh`、`build-ios-mac.sh`（项目根）
- `CoreClrDemo/IOSClrDemo`（A 阶段实验载体）
