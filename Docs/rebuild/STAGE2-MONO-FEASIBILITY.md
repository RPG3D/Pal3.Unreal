# Stage 2 可行性：.NET 11 上的 Mono 移动端

日期：2026-10-21
实验分支：插件 `mono-merge`（WIP 提交 `204fe715`），基线 `mobile20261021` / `8bb4522c`

## 结论（先说结果）

在 .NET 11 上构建 **Android/iOS 的 Mono 运行时被 SDK 明确禁止**：

```
error NETSDK1242: Building android projects with the Mono runtime is not supported
in .NET 11.0 and later. Use the CoreCLR runtime or target .NET 10.0.
```

触发位置（SDK 11.0.100-rc.1）：

`Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.TargetFrameworkInference.targets`

```xml
<Target Name="_CheckForUnsupportedMonoMobileRuntime"
        Condition="'$(_DisableCheckForUnsupportedMonoMobileRuntime)' != 'true'
                   and '$(UseMonoRuntime)' == 'true'
                   and $([MSBuild]::VersionGreaterThanOrEquals($(_TargetFrameworkVersionWithoutV), 11.0))
                   and ('$(TargetPlatformIdentifier)' == 'ios' or 'tvos' or 'maccatalyst' or 'android')">
  <NETSdkError ResourceName="MonoRuntimeNotSupportedForPlatform" ... />
</Target>
```

这不是"默认关闭"级别的信号，而是 Microsoft 在 .NET 11 里把移动端运行时统一到 CoreCLR 的
明确产品决策：**Mono 移动端在 11 上不受支持**。

## 内部旁路实验

检查带有一个内部开关 `_DisableCheckForUnsupportedMonoMobileRuntime=true`，且 dotnet/runtime
11 的 `src/mono/mono.proj` 仍保留 Android NDK / iOS 交叉编译逻辑，所以源码可能仍然可构建。
正在用该开关构建 `mono+libs -os android -arch arm64 -configuration Release`，结果待定。

即使构建成功：

- 每个托管构建都必须显式传这个内部属性（打包脚本、`dotnet publish`、UBT 触发的所有 dotnet 调用）；
- 该组合不受 Microsoft 支持，.NET 12 很可能直接移除；
- 不能作为给上游/团队的生产方案，只能作为实验。

## 对大一统方案的影响

| 方案 | 可行性 | 评价 |
| --- | --- | --- |
| 统一 net11 + Mono mobile（靠内部开关） | 取决于旁路构建结果 | 不受支持、需持续打补丁，不推荐 |
| **双 TFM 统一分支**：CoreCLR=net11，Mono=net10 | 可行 | Mono 沿用现有 net10 SDK；托管程序集按 flavor 构建两套；推荐 |
| 保持两条线（mobileclr net11 + mono net10） | 可行 | 最省事，但放弃"大一统" |
| 统一 net10 | 不可行 | 会丢掉 .NET 11 的 iOS 解释器默认支持（本阶段所有验证都基于 11） |

## 双 TFM 的具体做法（若选择）

- `UnrealSharp.Shared.props`：`TargetFramework` 按 `UseMonoRuntime` 条件选择
  （Mono → `net10.0`，CoreCLR → `net11.0`）。
- 打包命令按 flavor 传 `-p:UseMonoRuntime=true` 和对应 TFM；BCL 分别来自 MonoSDK(net10)
  与 CoreClrSDK(net11)。
- 插件 C++ 侧不受影响（flavor 宏与 SDK 选择已在本 WIP 中实现）。
- glue/用户程序集按 flavor 各构建一次；加载顺序文件不变。

## WIP 状态（`mono-merge@204fe715`）

已完成：
- MonoSDK 子模块 + `bUseMono` 构建期 flavor（`UnrealSharpCore.Build.cs`）
- 宿主三层分派：Mono → 移动 CoreCLR → 桌面 hostfxr
- 设置项、Mono 平台/架构路径助手、打包构建的 Mono 路径
- UAT 单实例重试（通用改进）、`PackageProjectMono` 动作
- 新增 Mono 运行时/宿主/加载上下文文件与打包命令

未完成：
- 托管侧 `Plugin`/`PluginLoader` 合并、编辑器热重载守卫、glue 生成器改动
- `PackageProjectMono` 对上游新 UAT API 的适配

分支未 push；父仓库指针仍指向 `8bb4522c`（阶段一）。
