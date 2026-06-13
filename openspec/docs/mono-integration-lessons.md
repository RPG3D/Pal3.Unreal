# Mono Integration Lessons Learned

从零开始把 Mono runtime 集成到 UnrealSharp 插件的完整踩坑记录。覆盖 Editor、Win64 packaged、Android APK 三个平台，及 Windows / macOS / iOS 的构建配置。

---

## 走过的弯路

这些是研究过程中尝试过但最终证明是错误的方向——记录下来防止重蹈。

### CSProjectUtilities.cpp 被误改后还原
尝试在 `CSProjectUtilities.cpp` 中为 Mono 添加 `FindFiles` 的 workaround，以为 Mono 下文件发现逻辑不同。后来发现 `IFileManager::FindFiles` 本身走 UFS，不需要特殊处理。**已完全还原为原始代码。**

### `--no-incremental` MSBuild 开关
`dotnet publish` 构建失败时，尝试加 `--no-incremental` 试图解决问题。这个开关对 MSBuild 无效，不符合 MSBuild 语法。**已从 `BuildManagedCode.bat` 中移除。**

### `dotnet publish` exit code 不可靠
`dotnet publish` 经常返回非零 exit code 但实际构建成功（MSBuild 对 file-locked 项目有内部重试机制）。最初根据 exit code 判断构建失败，导致误报。**改为直接验证输出 DLL 是否存在，不依赖 exit code。**

### Win64 native DLL 加载方案演变
- 最初尝试用 `mono_dl_fallback_register()` 拦截 Win64 的 P/Invoke 加载（模仿 iOS 方案）
- 后来发现 Win64 下直接复制 native DLL 到 exe 同目录更简单可靠（模仿 Android 方案）
- **最终方案：`MonoSDK.Build.cs` 用 `$(BinaryOutputDir)` 复制 `*Native*.dll` 和 `coreclr.dll` 到 `Binaries/Win64/`**

### PackageProjectMono 被放弃
最初计划用 `PackageProjectMono` C# 工具统一处理 BCL 复制 + 去重 + UAT 调用。后来发现：
1. `PackageProjectMono` 不在 UAT 已知命令中，无法通过 UAT 调用
2. BCL staging 完全可以通过 `MonoSDK.Build.cs` 的 `RuntimeDependencies` 自动处理
3. 项目 DLL staging 用 `dotnet publish` + `cp` 更简单

**最终方案：`dotnet publish` + 手动 cp + UAT 三步流程。**

### Path Strategy 的三次迭代
1. **第一版（全绝对路径）** — 到处用 `FPaths::ConvertRelativePathToFull`。在 Editor 下工作，但打包后 PAK 内 DLL 完全找不到。
2. **第二版（全相对路径）** — 去掉所有 `ConvertRelativePathToFull`。用户 DLL 加载正常了，但 BCL native DLL（`System.Globalization.Native.dll` 等）DllImport 失败——因为 Mono 底层用系统 `LoadLibrary` 加载，不走 UE I/O，无法解析相对路径。
3. **第三版（最终）** — BCL RuntimeDir 保留绝对路径（Mono 内部需要），User DLLs 用相对路径（UE I/O 处理 PAK）。

---

## 1. Path Strategy: Absolute vs Relative

**最重要的教训。** 不能用统一策略——谁加载文件决定路径格式。

| File Type | Loader | Path Format | Reason |
|-----------|--------|-------------|--------|
| **BCL Native DLLs** (`*Native*.dll`, `coreclr.dll`) | System `LoadLibrary`/`dlopen` (DllImport/P/Invoke) | **Absolute** | Non-managed code; loaded by OS, not UE I/O. Staged to `Binaries/{Platform}/` alongside exe. |
| **BCL Managed DLLs** (`System.Private.CoreLib.dll`, etc.) | `mono_install_assembly_preload_hook` → `LoadAssemblyFromUFS` → `FFileHelper::LoadFileToArray` | Relative OK | Managed assemblies; can go through UE I/O → UFS/PAK. |
| **User DLLs** (project scripts in PAK) | `FFileHelper::LoadFileToArray` → UE I/O | **Relative** | Inside PAK; OS can't see them. UE I/O resolves through UFS mount point. |
| **Android APK** | Android system APIs | Absolute OK | Android handles APK path mapping internally. |
| **Editor** | All on real filesystem | Absolute OK | `FPaths::ConvertRelativePathToFull` works fine. |

**关键洞察**: BCL `runtime/` 目录里的 native DLLs 走系统 `LoadLibrary`（不是托管 DLL），不走 UFS。`mono_set_assemblies_path()` 必须收到 OS 能解析的路径。User DLLs 反之——在 PAK 内必须用相对路径，让 `FFileHelper::LoadFileToArray()` 经 UFS 加载。

---

## 2. `mono_runtime_invoke` Parameter Passing

Mono 的 `mono_runtime_invoke` 按参数类型区分传递方式：

```
args[i] = POINTER VALUE directly    → unsafe pointer (char*, void*, struct*)
args[i] = POINTER TO VALUE (&var)   → value type (IntPtr, nint, int, bool)
```

**关键 Bug**: `FCSManagedCallbacks*`（struct 指针）看起来像指针，但 Mono 把它当值类型处理。直接传 `&ptr`（pointer-to-pointer）会导致 C# 端收到 `NullReferenceException`：
```cpp
// WRONG:
args[i] = &managedCallbacks;

// CORRECT: 先转 intptr_t，再传 &intptr_t_var
intptr_t managedCallbacksIntPtr = (intptr_t)&managedCallbacks;
args[i] = &managedCallbacksIntPtr;
```

---

## 3. Editor 启动崩溃：4 个根因

1. **`.csproj` DefineConstants 覆盖**: `<DefineConstants>WITH_EDITOR</DefineConstants>` **覆盖**（不是追加）Directory.Build.props 继承的 `UNREALSHARP_MONO`。Fix: `<DefineConstants>$(DefineConstants);WITH_EDITOR</DefineConstants>`
2. **检查 .NET SDK**: `UnrealSharpCore.cpp` 的 `StartupModule()` 调用 `VerifyCSharpEnvironment()` 检查 .NET SDK 是否安装——Mono 不需要 SDK。Fix: `#if WITH_EDITOR && !UNREALSHARP_MONO` 跳过
3. **UHT 不传递 UNREALSHARP_MONO**: UHT 插件独立编译（`.ubtplugin.csproj`），不继承 UBT `PublicDefinitions`。Fix: `BuildUtilities.cs` 直接读 `DefaultEngine.ini` 的 `bUseMono`
4. **`mono_runtime_invoke` 参数传递错误**: 见第 2 节

---

## 4. UAT 打包流程：Game glue 顺序是关键

### 必须先 Game C++ build，再 dotnet publish

UHT 生成 C# glue 代码（`.generated.cs`）时，Editor 目标产生包含 EditorOnly 属性（如 `ArrowComponent`）的 glue，Game 目标产生不含这些属性的 glue。**顺序错了意味着 managed DLL 带着 EditorOnly 绑定打包，运行时段访问空指针 → 崩溃。**

```
正确: Game C++ build → 生成 Game glue → dotnet publish → UAT Cook+Pak
错误: dotnet publish → Game C++ build (DLL 里是 Editor glue，EditorOnly 属性在 Game 中不存在)
```

### Build 与 skipBuild 的选择

UAT `BuildCookRun -Build` 会调用 UBT 编译 C++。如果 C++ 已编译且无改动，`-Build` 很快（缓存命中）。

**安全方案**（两种均可）:
- **方案 A**: C++ 单独编译 → 复制 managed DLL → UAT `-skipBuild` Cook+Pak+Stage（最安全，BuildWin64.bat 用此方案）
- **方案 B**: C++ 单独编译 → 复制 managed DLL → UAT `-Build` Cook+Pak+Stage（BuildAndroid.bat 用此方案，`-Build` 因 C++ 已编译而快速通过）

两种方案的**共同要点**：managed DLL 必须在 UAT Cook 之前就位，无论用什么 build 模式。

---

## 5. `dotnet publish` 的坑

- **Plugins/StaticVars 缺失**: `-p:UETargetType=Game` 时 `dotnet publish` 不输出这两个 DLL。Fix: `BuildManagedCode.bat` publish 后验证 8 个核心 DLL 是否存在
- **exit code 不可靠**: MSBuild 对 file-locked 项目有内部重试，返回非零但构建成功。Fix: 不依赖 exit code，直接检查输出文件
- **LoadOrder.json 不生成**: `dotnet publish` 不输出 `UserCode.LoadOrder.json`。Fix: 手动 `cp` 到目标目录
- **UBT 缓存**: 旧的 RuntimeDependencies 可能被缓存，导致新文件不被 staged。Fix: 必要时清 `Intermediate/Build`

---

## 6. BCL vs Project DLL 分层 & Staging

| 层级 | 阶段方式 | 位置 | 热更新 |
|------|----------|------|--------|
| **BCL** | `MonoSDK.Build.cs` `RuntimeDependencies.Add(..., StagedFileType.NonUFS)` | PAK 外 | 不支持 |
| **Project DLLs** | `dotnet publish` → `Content/Managed/{Platform}/` → `MonoSDK.Build.cs` `StagedFileType.UFS` | PAK 内 | 支持 |

各平台 native 文件 staging:
- **Win64**: native DLLs → `$(BinaryOutputDir)` → `Binaries/Win64/`（与 exe 同目录，OS 可解析）
- **Android**: native `.so` → `MonoSDK_APL.xml` → APK `lib/arm64-v8a/`
- **iOS**: native interop dylibs → `Mono.embeddedframework.zip` → IPA `Frameworks/`

---

## 7. 平台命名不一致

`FPlatformProperties::PlatformName()` 返回 `"Windows"`，但 MonoSDK 目录是 `"Win64"`。在多个文件中出现过因为名字不匹配找不到目录的问题。

**Fix**: 统一用 `PLATFORM_` 宏映射（`#if PLATFORM_WINDOWS → "Win64"`），避免依赖 `FPlatformProperties`。

---

## 8. Android: `bPackageDataInsideApk=true` + `bUseExternalFilesDir=true`

不设置此选项，UAT 把数据分到 OBB。Rider debug deploy 只推 APK 不推 OBB，导致运行时缺数据。

**Config** (`DefaultEngine.ini`):
```ini
[/Script/AndroidRuntimeSettings.AndroidRuntimeSettings]
bPackageDataInsideApk=true
bUseExternalFilesDir=true
```
两个都必要：`bPackageDataInsideApk` 确保数据不进 OBB；`bUseExternalFilesDir` 让 `ProjectSavedDir()` 返回包名目录而非 SD 根目录。

**Engine fix** (`AndroidPlatform.Automation.cs`): 两处硬编码 `bIsDataDeploy = true` 改为 `bIsDataDeploy = !Deploy.GetPackageDataInsideApk()`。

---

## 9. MobileHDR `checkSlow(bInitialized)` Crash

`ReadOnlyCVARCache.cpp` 里 `MobileAllowFramebufferFetchIniValue()` (L91) 调用栈触发 `MobileHDR()`，但 `bInitialized=true` 在 L160 才设置。Debug 构建 `checkSlow()` 断言失败。

**Fix**: 移除早期退出条件中的 `&& MobileHDR()`（UE 5.8 backport 修复）。

---

## 10. MSVC C4819: UTF-8 Source Without BOM

Windows code page 936（简体中文）下，MSVC 对无 BOM 的 UTF-8 源文件报 C4819。Mono SDK 用 `/WX`（warnings as errors），编译中断。

**Fix**: `src/mono/CMakeLists.txt` 添加 `/utf-8` 标志（必须在 `/W4` 和 `/WX` 之前）。

---

## 11. Editor Hot-Reload under Mono

CoreCLR 用 Roslyn 做 hot-reload，Mono 不支持 Roslyn：
- `CSHotReloadSubsystem.cpp`: Mono 路径跳过 `LoadSolutionAsync`，改用 `dotnet build`
- `UnrealSharpEditor.cpp`: 跳过 `LoadPluginAssemblyByName("UnrealSharp.Editor")`；`PackageProject` 加 `TargetPlatform` 参数

---

## 12. Override 目录 & `PurgeStaleOverrideDlls`

### 路径规则

Override 目录基于 `FPaths::ProjectSavedDir()/Managed/{Platform}/`。

| 平台 | Override 路径 |
|------|-------------|
| **Win64** | `<Project>/Saved/Managed/Win64/` |
| **Android** | `/sdcard/Android/data/<pkg>/files/UnrealGame/<Project>/<Project>/Saved/Managed/Android/`（双层 ProjectName） |

Android 必须配置 `bUseExternalFilesDir=true`，否则 `ProjectSavedDir()` 返回 SD 卡根目录（`/sdcard/UnrealGame/...`）而非包名目录：

```ini
[/Script/AndroidRuntimeSettings.AndroidRuntimeSettings]
bPackageDataInsideApk=true
bUseExternalFilesDir=true
```

### 推送命令

```bash
# Android — 先用 logcat 确认引擎日志中的 "Override search dir (Saved):" 路径
adb push ManagedSharpDemo.dll /sdcard/Android/data/<pkg>/files/UnrealGame/<Project>/<Project>/Saved/Managed/Android/
```

### PurgeStaleOverrideDlls

比较 DLL 和可执行文件（Android: `libUnreal.so`）的时间戳，删除比 exe 旧的 DLL。**push 后必须 `touch` 更新 DLL 时间戳**，否则每次启动都被清理：

```bash
adb push ... && adb shell touch .../ManagedSharpDemo.dll
```

---

## 13. 调试技巧

- **Mono 异常定位**: C# catch 块写异常到 `/tmp/UnrealSharp_InitException.txt`，C++ 端 `InitializeMonoHost()` 读取
- **验证编译标志**: 用 `strings` 命令检查 DLL 是否包含 Mono 特定字符串（确认 `UNREALSHARP_MONO` 已定义）
- **如果没有异常文件**: 说明 `UNREALSHARP_MONO` 在 C# DLL 中未定义——检查 `.csproj` 的 DefineConstants 是否被覆盖
- **Mono trace level**: `mono_trace_set_level_string("debug")` 输出详细日志；生产环境用 `"warning"`
- **Win64 DllImport 调试**: 编译 debug Mono SDK，在 `native-library.c` 中设断点跟踪 `LoadLibrary` 调用

---

## 14. iOS 特殊处理

- **AOT**: `mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP)` + `mono_aot_register_module(System.Private.CoreLib)`
- **DL fallback**: `mono_dl_fallback_register()` 拦截 BCL native lib 的 P/Invoke 加载
- **Globalization**: `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`
- **静态链接**: `.a` 用绝对路径（避免 dyld `@rpath` 解析失败）
- **Components**: 全部 stub 变体，仅 `marshal-ilgen` 用完整版

---

## Summary Checklist

- [ ] BCL native DLLs → absolute path, alongside exe（系统 `LoadLibrary` 需要）
- [ ] User DLLs → relative path, inside PAK（UE I/O 处理 UFS）
- [ ] `mono_runtime_invoke` args: struct pointers → cast to `intptr_t` then `&var`
- [ ] `.csproj` defines: `$(DefineConstants);NEW_VALUE`（append，不是 replace）
- [ ] UHT: 在 `BuildUtilities.cs` 中直接读 ini 注入 `UNREALSHARP_MONO`
- [ ] Editor: `#if WITH_EDITOR && !UNREALSHARP_MONO` 跳过 .NET SDK 检查
- [ ] UAT packaging: managed DLL 必须在 Cook 之前就位
- [ ] `bPackageDataInsideApk=true` + `bUseExternalFilesDir=true` for Android
- [ ] `/utf-8` for MSVC Mono SDK builds（code page 936 兼容）
- [ ] Platform naming: 用 `PLATFORM_` 宏，不要用 `FPlatformProperties::PlatformName()`
- [ ] 打包顺序：先 Game C++ build（生成 Game glue），再 `dotnet publish`
- [ ] `dotnet publish` with `-p:UseMonoRuntime=true --no-self-contained`
- [ ] `dotnet publish` exit code 不可靠，直接检查输出文件
- [ ] Android override push 后 `touch` 更新时间戳，否则 PurgeStaleOverrideDlls 删除
- [ ] Mono trace level: `"warning"` for production, `"debug"` for debugging
