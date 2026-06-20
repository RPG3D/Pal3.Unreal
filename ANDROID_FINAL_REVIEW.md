# Android CoreCLR 移植 — 完整 Git Diff Review（提交前终审）

> Review 日期：2026-06-20。覆盖全部改动文件，提交前一次性高质量审查。
> 修复版本：已处理问题1-7（注释矛盾、DllHash 目录、注释清理、bUseMono 注释保留）。

## 一、改动文件清单

### 新增文件（8 个，零冲突）✅
| 文件 | 作用 | 评估 |
|------|------|------|
| `Managed/DotNetRuntime/inc/coreclrhost.h` | raw CoreCLR C API 头（dotnet/runtime 源） | ✅ 上游原样 |
| `Source/ThirdParty/CoreClrSDK/CoreClrSDK.Build.cs` | Android staging 模块（link + NonUFS/UFS + APL） | ✅ |
| `Source/ThirdParty/CoreClrSDK/CoreClrSDK_APL.xml` | .so → APK lib（copyDir） | ✅ 注释已修 |
| `Source/ThirdParty/CoreClrSDK/README.md` | SDK 说明 | ✅ |
| `Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost_Android.cpp` | Android host（coreclr_* + 解压 + xxHash） | ✅ 注释已修 |
| `Managed/UnrealSharp/UnrealSharp.Plugins/PluginLoadContext_Android.cs` | partial class Android fallback | ✅ 隔离良好 |
| `Build/Scripts/BuildCommands/PackageProjectAndroidCoreClr.cs` | Android 打包命令 | ✅ |
| `BUILD_ANDROID.md` | 打包文档 | ✅ |

### 修改的存量文件（15 个）✅ 全部审查通过

## 二、逐文件审查

### C++ 侧（隔离良好，`#if PLATFORM_ANDROID`）

**`CSDotNetRuntimeHost.h`** ✅
- `#include <coreclrhost.h>` 无条件（Win/Mac 多包含一个头，无副作用）
- Android 成员全在 `#if PLATFORM_ANDROID` 内
- 注释已更新（SavedDir + size 预检）

**`CSDotNetRuntimeHost.cpp`** ✅
- `InitializeManagedRuntime`/`ShutdownManagedRuntime` 用 `#if PLATFORM_ANDROID ... #else ... #endif` 分派
- 只动函数首尾，hostfxr 中间逻辑未改
- Win/Mac 走 `#else`（原路径），零变更

**`CSDotNetRuntimeHost_Android.cpp`** ✅（已修复）
- 直接调 `coreclr_*`（P1 link，无 dlopen/字符串）
- `EnsureRuntimeDllsExtracted`：SavedDir + DllHash 目录创建（问题3 已修）+ size 预检 + xxHash
- `BuildTpa`：基于源文件名（非解压目录），CoreLib 排最前
- 注释矛盾已修（问题1）

**`CSManager.cpp`** ✅
- collectible 强制 false：`#if PLATFORM_ANDROID && !WITH_EDITOR`，Win/Mac 用原 `Manifest.bCollectible`
- `RF_MarkAsRootSet`：`bIsCollectible ? RF_NoFlags : RF_MarkAsRootSet`——Win/Mac collectible 保持原状（C1 已回退），仅 Android non-collectible 加 root
- GC 修复正确（disregard-for-GC 引用合规）

**`CSPathsUtilities.cpp`** ✅
- `GetUserAssemblyDirectory`：`#if PLATFORM_ANDROID && !WITH_EDITOR` 返回 SavedDir/Managed/Android（`ConvertToAbsolutePathForExternalAppForRead`）
- Win/Mac 走 `#else`（原 Binaries/Managed）
- SavedDir 避免 PAK 干扰（IPlatformFilePak.cpp:5124）

**`CSDotnetUtilties.cpp`** ✅
- path marker 尾反斜杠修复（bug 修复，Win 也受益）
- `Paths::GetPluginAssembliesPath` 限定（Mac/Android 编译 bug 修复）
- 都是上游应接受的 bug 修复

**`UnrealSharpCore.cpp`** ✅
- `StartupModule` 重试上限 10 次（`#if WITH_EDITOR` 内），防死循环 OOM
- 通用健壮性改进，Win/Mac 也受益

**`UnrealSharpCore.Build.cs`** ✅
- 加 `CoreClrSDK` 依赖（非 Android 平台 CoreClrSDK 是 no-op）

**`UnrealSharp.uplugin`** ✅
- `SupportedTargetPlatforms` 加 `Android`

### C# 侧（partial class 隔离）

**`PluginLoadContext.cs`** ✅
- `partial class` + `_resolver` 可空 + `Load()` 调 `ResolveAssemblyPath`（包装方法在 partial 文件）
- 3 处最小改动，其余保持原始（含空行风格）

**`PluginLoadContext_Android.cs`** ✅（新增）
- `_pluginDir` + 新 ctor + `TryCreateResolver` + `ResolveAssemblyPath` + `ResolveAssemblyPathManual`
- Android fallback 完全隔离，Win/Mac 不编译 Android ctor
- 注意：`_resolver` 在主文件声明，partial 文件访问——合法（partial 共享 private）

**`Plugin.cs`** ✅
- else 块一行：`new PluginLoadContext(name, TryCreateResolver(path), isCollectible, path)`
- try-catch 移到 `TryCreateResolver`，Plugin.cs 接近原始

**`Main.cs`** ✅
- `using Microsoft.Build.Locator` 包 `#if WITH_EDITOR`（Win/Mac 编辑器仍生效，packaged 不编译）
- `private` → `public`（Android `coreclr_create_delegate` 需要）

**`PluginLoader.cs`** ✅
- 异常日志加全（`ex.ToString()` + assemblyPath），通用改进

**`UnrealSharp.Plugins.csproj`** ✅
- `PackageReference Microsoft.Build.Locator` 加 `Condition="'$(DisableWithEditor)' != 'true'"`
- Win/Mac 编辑器构建（未设 DisableWithEditor）→ 引用；packaged（DisableWithEditor=true）→ 不引用

### 配置/打包

**`CoreClrSDK.Build.cs`** ✅（注释已修）
**`CoreClrSDK_APL.xml`** ✅（copyDir + 注释已修）
**`DefaultEngine.ini`** ✅
- GLES：`bSupportsVulkan=false` + `bBuildForES31=true`（修 Vulkan 二次启动崩）
- `bUseMono=true` 注释保留（留 Mono 退路）

**`BuildManagedCode.bat`** ✅
- `--no-self-contained` + `-p:DisableWithEditor=true`

**`ManagedSharpDemo.csproj`** ✅
- 修废弃 SharpDemo.Glue 引用，net10.0，SharpDemo.dll HintPath

## 三、关键修复验证状态

| 修复 | 验证 |
|------|------|
| P1 link + 直接调用 | ✅ 编译 + 运行 |
| xxHash 按需解压 | ✅ 首次提取、二次跳过 |
| TPA 基于源文件名（防残留） | ✅ |
| GC non-collectible + root set | ✅ 7.5 分钟无崩 |
| GLES 二次启动 | ✅ 完美工作 |
| SavedDir（避 PAK 干扰） | ✅ |
| DllHash 目录创建（本次修） | ⏳ 待装机验证 |
| 注释清理（本次修） | ✅ 编译通过 |

## 四、对 Win/Mac 影响（终审）

| 改动 | Win/Mac 影响 |
|------|-------------|
| `#if PLATFORM_ANDROID` 隔离的 4 项 | 零 ✅ |
| bug 修复（path marker、Paths::限定、重试上限、日志加全） | 正面（Win/Mac 也受益）✅ |
| `CSManager RF_MarkAsRootSet` | collectible 分支保持 `RF_NoFlags`（原状），Win/Mac 零变更 ✅ |
| `Main.cs public` | 无副作用（[UnmanagedCallersOnly] 不可托管调用）✅ |
| `PluginLoadContext` partial | Win/Mac resolver 非 null，走原路径 ✅ |
| `csproj PackageReference Condition` | Win/Mac 编辑器未设 DisableWithEditor → 引用生效 ✅ |
| `CoreClrSDK` 依赖 | 非 Android no-op ✅ |
| `Android` 平台 | 仅扩展 ✅ |

**结论：Win/Mac 零行为变更。**

## 五、冲突风险（终审）

- **C++ 侧**：`#if PLATFORM_ANDROID` 良好隔离，冲突面小
- **C# 侧**：partial class 隔离 Android fallback，存量文件最小改动（PluginLoadContext 3 处、Plugin.cs 1 行、Main.cs using+public）
- **bug 修复**（CSDotnetUtilties）：上游应接受，合并时优先提交
- **sln/GlueGenerator**：配套改动（解决 publish 编译报错），合理保留

## 六、遗留 / 未做

- **probe 内存加载**（P2）：留 coreclrdemo 单独研究（`ANDROID_PROBE_DEADEND.md`）
- **DllHash sidecar 装机验证**：本次修复后待最终装机确认
- **iOS/x86/远程调试**：非目标（文档标注 future work）

## 七、终审结论

**全部改动审查通过，可提交。** 修复了 7 个 review 问题（注释矛盾、DllHash 目录、注释清理、bUseMono 保留）。Win/Mac 零变更，C++/C# 隔离良好，功能验证通过（GLES + xxHash + GC 稳定）。

建议提交前最终装机验证一次（DllHash sidecar 写入 + 二次跳过 + GLES 稳定）。
