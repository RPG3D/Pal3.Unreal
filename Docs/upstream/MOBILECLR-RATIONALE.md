# MobileClr 线提交思路与上游化评估

记录 `mobileclr202609` 相对上游基线 `b0bd7c62` 的每个提交：为什么存在、改了什么、
是否适合上游。目的有两个：给上游作者解释设计取舍；让我们自己在合并/重放时知道
哪些提交可以丢、哪些必须保留。

## 0. 总体数据结构（2026-10-21）

```
45 files changed, 1920 insertions(+), 56 deletions(-)
├── 新增文件 11 个        1520 行   ← 不与上游冲突（除同名文件）
└── 修改上游文件 34 个     400 行   ← 真正的合并面
    ├── 纯 TFM/SDK 版本号 10 个文件 × 1 行
    ├── NativeCallbacksWrapperGenerator   186 行（本次后约 270）
    ├── CSDotNetRuntimeHost.cpp            48 行
    └── 其余 21 个文件                     约 90 行
```

`Source/ThirdParty/CoreClrSDK` 是子模块，脚本与 CI 不计入插件的文件 diff。

## 1. 提交清单

| # | 提交 | 主题 | 为什么需要 | 上游? |
| --- | --- | --- | --- | --- |
| 1 | `fb3ba662` | MobileClr 集成（单提交） | 在 `.NET 11` 上把 Android/iOS 的裸 CoreCLR 宿主接进来：`CSDotNetRuntimeHost_Mobile.cpp`（PAK 解包、TPA、host_runtime_contract、错误写入桥）、`PackageProjectMobile.cs`、`PluginLoadContext_Android.cs`、CoreClrSDK 子模块、三份排障文档 | 移动宿主是**新特性**，不混进 fix PR；可单独开 feature 分支再谈 |
| 2 | `39a8aa9c` | 子模块指针 + CI 子集裁剪 | SDK 只构建移动端需要的 subset，缩短 CI 时间 | 否（属于我们的 SDK 仓库） |
| 3 | `3623f6e8` | 子模块 URL | 指向 RPG3D fork | 否 |
| 4 | `3767c77b` | CoreClrSDK 布局对齐 MonoSDK | `<Platform>/<arch>` 目录约定、RID 区分 `ios-arm64` / `iossimulator-arm64`、设备包不再误用模拟器 RID | 否（SDK 仓库） |
| 5 | `3f5074c1` | macOS hostfxr 修复 | 路径分隔符只在 Windows 归一化；`char8_t` 强转；文档补构建顺序 | **部分已上游**：`char8_t` 部分已在 `901cfce5`；**路径分隔符仍可提 PR** |
| 6 | `6696a931` | 分支/补丁策略文档 | 固定「一次同步一个分支」「一个提交一个关注点」「子模块指针单独放最后」 | 否（内部约定） |
| 7 | `0d1a5459` | iOS 私有 PCH + framework 签名 | iOS unity 分组与 Android 不同，必需；PCH 从 Mono 线移植而来 | **可上游**（`PR-03`） |
| 8 | `2fb28086` | iOS 运行时首通 | `NATIVE_DLL_SEARCH_DIRECTORIES` 指向 `CoreCLR.framework/Frameworks`；字符串 calli 的 UTF-8 处理 | 宿主部分否；生成器部分见 `PR-01` |
| 9 | `f39c20b0` | iOS bring-up 文档 | 记录设备/模拟器差异、签名、解释器限制 | 否 |
| 10 | `a5fcfcf4` | calli ABI twin | 解释器拒绝需要 marshalling 的 calli（string/ref-out/bool） | **可上游**（`PR-01`，收益最大） |
| 11 | `7890c97b` | 子模块指针 | bash 3.2 空数组展开修复 | 否 |
| 12 | `e610cf73` | 去掉 `DOTNET_Interpreter` 覆盖 | iOS 本来就必须走解释器；强制设置只对不存在的 SDK 变体有意义；记录性能结论 | 否 |
| 13 | `2c2a1938` | NUL 终止 + stackalloc + glue 守卫 | 修复本次评审发现的越界读；字符串路径改为零分配快路径；cook 不再改写生成的 glue | **可上游**（`PR-01` / `PR-04`） |

## 2. 关键设计决策

### 2.1 为什么用 ABI twin，而不是改绑定声明

把 18 处 `string` 参数直接改成 `char*` 会波及所有调用点，且会让托管侧 API 失去
类型安全。生成器在 wrapper 边界做一次转换，调用点零改动，并且 public 签名保持
`string`。注册表的 size key 也不变（`string`/`char*` 都是 8 字节，`bool`/`byte` 都是 1），
所以 native 侧的绑定查找逻辑完全不用动。

### 2.2 为什么 `bool` 要变成 `byte`

CLR 对 `delegate* unmanaged<..., bool>` 的默认 marshalling 是 4 字节 Win32 `BOOL`，
而插件里所有 native bind 用的都是 C++ `bool`（1 字节）。传参靠只读低字节「碰巧正确」，
返回值则可能读到未定义的高位。改成 `byte` 后与 native ABI 精确一致 —— 这不是为了
iOS 妥协，而是修正了一个 JIT 平台上同样存在的隐患。

### 2.3 为什么字符串必须 NUL 终止

native 侧按 C 字符串读取（`FName(const ANSICHAR*)`、`UTF8_TO_TCHAR`、`UE_LOGFMT`）。
`Encoding.UTF8.GetBytes` 返回精确长度数组、不追加 `\0`，因此在 ABI twin 里必须
自己写终止符。旧路径由 CLR marshaller 负责，它总是分配带终止符的缓冲区；手工
编码后这个保证需要显式补回。短字符串（≤256 字节）用 `stackalloc`，既补齐终止符
又消除了每次调用的堆分配。

### 2.4 为什么移动端逻辑都放在新文件里

`CSDotNetRuntimeHost_Mobile.cpp`、`PluginLoadContext_Android.cs`、
`UnrealSharpSharedPCH.h` 都是新增文件，上游合并时不会产生冲突；
共享文件只保留必要的 dispatch 和 guard。这条约定写在 `DOWNSTREAM.md` 里，
后续改动也应遵守。

### 2.5 为什么 SDK 用子模块 + CI 构建

本地构建 dotnet/runtime 的移动端产物耗时长、依赖多，且容易和团队其他人的环境
不一致。SDK 的构建、布局、签名全部放在 `CoreClrSDK` 仓库，通过 GitHub Actions
产出，插件只 pin 一个提交号。这样插件的 diff 里完全不出现 SDK 脚本。

### 2.6 为什么不再强制 `DOTNET_Interpreter`

SDK 已经按 `FeatureDynamicCodeCompiled=false` 构建，iOS 上 `EnableInterpreter()`
是无条件执行的；再设置环境变量只会让「代码假设」和「SDK 实际行为」脱节。
去掉覆盖后，设备上仍然没有 `InvalidProgramException`。

## 3. 剩余问题

- iOS 上 `M_Eye_Occlusion` 的 Metal shader 编译失败，回退到默认材质；属于内容侧
  问题，与运行时无关。
- `NativeCallbacksWrapperGenerator` 目前不支持 `string` 返回值与 `ref/out string`；
  已加 `US0015` 编译期警告。若上游新增此类绑定，需要先设计对应的 twin 形态。
- ABI twin 的 `ref/out` 指针不 pin（与原 `ref T` 的 calli 语义一致）；
  当前调用点都在启动期、native 不回调托管代码，因此安全；若将来有长时调用，
  需要重新评估。

## 4. 给上游的边界

我们会请求接收：`PR-01`（生成器 ABI twin）、`PR-02`（Windows 路径）、
`PR-03`（iOS PCH）、`PR-04`（glue commandlet 守卫）。

不请求接收：移动宿主、CoreClrSDK、打包命令、.NET 11 TFM 升级、分支文档。
这些属于 fork 的产品线，保持独立演进；等上游对移动端有兴趣时再单独讨论。
