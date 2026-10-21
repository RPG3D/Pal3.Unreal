# Stage 1：CoreCLR 移动端移植到 upstream/main

日期：2026-10-21
插件分支：`mobile20261021`（基于 `upstream/main@971c81c3`）
提交：`8bb4522c feat(mobile): port CoreCLR mobile hosting onto upstream main`（单提交，未 push）

## 方法

按"逐个 diff 审阅"的方式重建，而不是 rebase：

- **上游未改动过的文件**：直接取旧分支的版本（`git diff b0bd7c62..upstream/main -- <file>` 为空即证明该文件
  在两边基线一致，搬运是精确的）。
- **上游动过的 9 个文件**：读上游版本后手工合并，逐条判断保留/改造/丢弃。
- 不采用机械 rebase 的结果。

## 丢弃的改动（审阅结论）

| 旧分支改动 | 原因 |
| --- | --- |
| `CSDotNetRuntimeHost.cpp` 的 `char8_t` 强转 | 上游 `901cfce5` 已吸收 |
| `CSManager.h` 的 `UNREALSHARPCORE_API static … Instance` | 上游已含 |
| `GlueGenerator.cs` 的 Timestamp 代码块 | 整段被注释，无行为变化 |
| `BuildUtilities.cs` / `FunctionUtilities.cs` 的空白改动 | 仅空格 |

## 手工合并的关键点

- `Main.cs`：在上游重写后的 MSBuild-locator 版本上，把 `InitializeUnrealSharp` 改为 `public`（`coreclr_create_delegate` 需要）。
- `CSManager.cpp`：移动端非 collectible ALC + `RF_MarkAsRootSet`，叠加在上游重写的初始加载流程上。
- `CSDotNetRuntimeHost.cpp`：移动分派 + 仅 Windows 的路径分隔符归一化；`char8_t` 不再重复。
- `CSHotReloadUtilities.cpp`：补 `UObject/UObjectIterator.h`，`TObjectPtr<UEdGraphNode>&` 循环修复。
- `.uplugin`：在上游平台列表上追加 Android/IOS。
- `PackageProjectMobile.cs`：上游 `BuildUserGlue.Build` 新增了 `addReferences` 参数，改为
  `(..., publishFolder, false, buildArguments)`。

## 本次审出的语义漂移（rebase 不会报的）

1. **UAT API 变化**：`BuildUserGlue.Build` 的第 5 个参数从 `IList<string>` 变成了 `bool addReferences`。
   文本合并没有冲突，但调用会编译失败（`CS1503`）。
2. **构建顺序的前置条件**：清掉 `Intermediate/UnrealSharp/UHT` 后，必须先做 **Game 目标** UBT 构建生成
   `UHT/Game`，`PackageProjectMobile` 才能编译绑定工程；否则生成器解析不到引擎类型，产出带空命名空间的
   glue（`CS0234`/`CS8802`）。顺序固定为：Editor 构建 → Game 构建（目标平台）→ PackageProjectMobile → BuildCookRun。

## 验证结果

| 项 | 结果 |
| --- | --- |
| `SharpDemoEditor Mac Development`（UHT 重生成） | 通过 |
| 插件托管解决方案（Release） | 0 warning / 0 error |
| `ManagedSharpDemo` + UAT `BuildUserSolution` | 通过 |
| `SharpDemo Android Development`（Game 目标，含移动宿主 C++） | 通过 |
| Android `PackageProjectMobile` + `BuildCookRun` | APK 460 MB |
| iOS `PackageProjectMobile` + `BuildCookRun` | `SharpDemo.app`，含签名后的 `CoreCLR.framework` |
| 真机运行 | **待做**：Android 设备未连接；iPad 正在使用 |

## 桌面（Mac）打包补记

`BuildCookRun` 本身只产出 `SharpDemo.app`，不包含 UnrealSharp 的托管负载。桌面端还需要一次
`PackageProject`，把自包含发布目录写进 **app 内部** 的项目目录：

```bash
AutomationTool.dll PackageProject \
  -ArchiveDirectory=<Packaged/Mac/SharpDemo.app/Contents/UE/SharpDemo> \
  -UETargetType=Game -UEBuildConfig=Development \
  -TargetPlatform=Mac -TargetArchitecture=x64
# 写入 .../Contents/UE/SharpDemo/Binaries/Managed/net11.0（249 个文件，含 hostfxr）
```

产物里带 `UnrealSharpBuild.flag`，运行时 `IsUnrealSharpInstalled()` 才会从 app 内的
`Binaries/Managed/net11.0` 取程序集。追加文件会使 UE 的签名失效，本地验证时用
`codesign --force --deep -s -` 重新 ad-hoc 签名。

实测（`-nullrhi -unattended`）：`SharpDemo.dll`、`ManagedSharpDemo.dll` 等从 app 包内加载，
地图 `LV_OpenWorld` 运行，世界里出现 `ScriptCharacter_C`，无 `Fatal`/`InvalidProgram`/`DllNotFound`。

## 环境备注

- `Script/Pal3.Core/Libraries` 的两个 DLL 是 gitignore 的本地依赖；缺失时解决方案 publish 会失败。
  本次从 NuGet 恢复了 `Newtonsoft.Json 13.0.3` 和 `INIFileParser 3.4.0`（net20），未改仓库。
- 父仓库的子模块指针暂未更新，等真机验证通过后再指向 `8bb4522c`。

## 阶段二入口条件

1. 本阶段两个移动平台的真机运行通过（Android + iPad）；Mac 桌面已实测通过。
2. MonoSDK 能产出 .NET 11 的 Android/iOS 产物（iOS AOT 是关键风险）。
3. 届时在 `mobile20261021` 上以纯增量方式加入 Mono flavor，不重构现有 CoreCLR 路径。
