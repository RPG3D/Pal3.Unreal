# Android 打包踩坑记录（CoreCLR host）

> 记录 UnrealSharp Android (arm64) 打包过程中遇到的所有坑，避免重复踩。
> 最后更新：2026-06-19
>
> ✅ **2026-06-19 完整跑通**：CoreCLR 启动 → 所有 assembly 加载 →
> C# 脚本执行 → 角色模型 + 动画蓝图加载。`AScriptCharacter` 的 EnhancedInput
> joystick + `StaticLoadObject(ParagonShinbi)` 全部工作。应用持续运行无崩溃。

## 1. 核心架构

Android **没有 hostfxr**，用 raw `coreclr_initialize` / `coreclr_create_delegate` C API 启动 CoreCLR。
- 实现在 `Plugins/UnrealSharp/Source/UnrealSharpCore/Private/DotNet/CSDotNetRuntimeHost_Android.cpp`
- 用 `#if PLATFORM_ANDROID` 派发，**没有开关、没有宏**，Android 是一等支持平台（和 Win64/Mac 一样）
- 参考 demo：`E:\Code\CoreClrDemo\AndroidClrDemo`

## 2. 打包流程（必须按顺序）

### 步骤 1: build Game target（生成 Game glue，**必须先做**）

```
"E:/Code/UE5/Engine/Build/BatchFiles/Build.bat" SharpDemo Win64 Development -Project="E:/UEProject/SharpDemo/SharpDemo.uproject" -WaitMutex
```

**坑**：`UnrealSharp.csproj` 里 `<Compile Include="$(UHTGeneratedPath)\**\*.cs"/>`，`UHTGeneratedPath = Intermediate\UnrealSharp\UHT\$(UETargetType)`。publish 时传 `-p:UETargetType=Game`，所以需要 `UHT\Game\` 下有生成的 `*.generated.cs`（`UWorldSubsystem`、`UInputAction`、`AWorldSettings` 等）。

- **Game glue 在项目级**：`E:\UEProject\SharpDemo\Intermediate\UnrealSharp\UHT\Game\`（不是插件内！）
- Editor build 只生成 `UHT\Editor\`，**必须 build Game target 才有 `UHT\Game\`**
- 如果删过 UHT 缓存，要重新 build Game target 才能恢复

参考 `E:\Code\UnrealSharp\README_MONO.md` 第 37 行：「Build the Game target (required once)」。

### 步骤 2: 编译托管 DLL

```
cd "E:/UEProject/SharpDemo" && cmd /c "BuildManagedCode.bat"
```

产物 → `Content/Managed/Win64/` + 拷贝到 `Content/Managed/Android/`。

### 步骤 3: UAT 打包

```
"E:/Code/UE5/Engine/Build/BatchFiles/RunUAT.bat" BuildCookRun -project="E:/UEProject/SharpDemo/SharpDemo.uproject" -Build -Target=SharpDemo -TargetPlatform=Android -ClientConfig=Development -Cook -Pak -Stage -Archive -package -nocompileuat
```

APK → `ArchivedBuilds/Android/SharpDemo-arm64.apk`

### 步骤 4: 装机测试

```
adb install -r ArchivedBuilds/Android/SharpDemo-arm64.apk
adb shell monkey -p com.YourCompany.SharpDemo -c android.intent.category.LAUNCHER 1
adb logcat -s CORECLR:I DOTNET:I LogUnrealSharp:I
```

## 3. 已踩的坑

### 坑 A: 文件锁 CS2012 / MSB3491
**现象**：`dotnet publish` 报 `Cannot open ...dll for writing -- being used by another process` 或 `Cannot create a file when that file already exists`。

**原因**：残留的 dotnet/VBCSCompiler 进程占用 obj/bin 文件，或并行编译竞态。

**解决**：
```
dotnet build-server shutdown
cmd /c "taskkill /F /IM dotnet.exe /T & taskkill /F /IM VBCSCompiler.exe"
# 删 obj/bin
find Plugins/UnrealSharp/Managed -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +
```
单线程 `-m:1` 能避免并行竞态，但根因还是进程残留。

### 坑 B: MSBuildLocator 在 Android 崩溃
**现象**：设备 logcat 报
```
System.InvalidOperationException: Path to dotnet executable is not set.
  at Microsoft.Build.Locator.MSBuildLocator.GetInstances(...)
  at UnrealSharp.Plugins.Main.InitializeUnrealSharp(...)
```
Android 上没有 dotnet SDK，MSBuildLocator 的静态 `Lazy` 初始化抛异常，`InitializeUnrealSharp` 返回 false → Fatal。

**根因 1**：`UnrealSharp.Plugins.csproj` 无条件 `<PackageReference Include="Microsoft.Build.Locator" />`，即使 `#if WITH_EDITOR` 代码块被剥离，程序集引用仍在 → 打包进 APK → 静态初始化触发。

**根因 2**：`DisableWithEditor=true` 没生效时，`#if WITH_EDITOR` 块（Main.cs 调 `MSBuildLocator.QueryVisualStudioInstances`）没被剥离。

**解决**：
1. `UnrealSharp.Plugins.csproj` 把 PackageReference 加条件：
   `<PackageReference Include="Microsoft.Build.Locator" Condition="'$(DisableWithEditor)' != 'true'" />`
2. `BuildManagedCode.bat` 的 publish 加 `-p:DisableWithEditor=true`

**验证** DLL 是否还引用 MSBuild（用 MetadataReader，`strings` 不可靠，.NET 元数据是 UTF-16）：
```csharp
// 用 System.Reflection.Metadata.PEReader 扫 AssemblyReferences
// 见 /tmp/refcheck/Program.cs
```

### 坑 C: WITH_EDITOR 剥离逻辑
`UnrealSharp.Plugins.csproj` / `UnrealSharp.StaticVars.csproj`:
```xml
<DefineConstants>WITH_EDITOR</DefineConstants>
<DefineConstants Condition="'$(DisableWithEditor)' == 'true'">$(DefineConstants.Replace('WITH_EDITOR;', '').Replace('WITH_EDITOR', ''))</DefineConstants>
```
**验证**：单独测过，`DisableWithEditor=true` 时 `DefineConstants` 变成 `;DEBUG`（WITH_EDITOR 被剥离），逻辑本身是对的。没生效通常是 publish 没传 `-p:DisableWithEditor=true` 或用了缓存。

### 坑 D: 编辑器 SDK 检测失败 → 对话框死循环 → OOM
**现象**：UAT cook 时 `UnrealEditor-Cmd` 内存暴涨到 OOM（8.1 GiB 分配失败）。

**根因**：`GetDotNetDirectory()` 扫 PATH 找 `Program Files\dotnet\`（**带尾反斜杠**），但 PATH 条目是 `C:\Program Files\dotnet`（无尾反斜杠）→ 永远找不到 SDK → `VerifyCSharpEnvironment()` 弹对话框 → `StartupModule()` 失败时**递归调用自己** → 死循环 → OOM。

**解决**：
1. `CSDotnetUtilties.cpp` 的 `PathMarker` 改成 `Program Files\dotnet`（去掉尾反斜杠）
2. `UnrealSharpCore.cpp` `StartupModule()` 的递归重试加 10 次上限

### 坑 E: 两个 UnrealSharp.sln 不一样
- `Plugins/UnrealSharp/UnrealSharp.sln` —— 项目根的，已清理废弃项目（用户清理过）
- `Plugins/UnrealSharp/Managed/UnrealSharp/UnrealSharp.sln` —— `BuildManagedCode.bat` 实际用的，还含 `UnrealSharp.Editor`/`UnrealSharp.GlueGenerator`/`UnrealSharp.CodeFixer` 等

两个 sln 项目列表不同。`BuildManagedCode.bat` 用的是后者。

### 坑 F: ManagedSharpDemo.csproj 引用废弃的 SharpDemo.Glue
`ManagedSharpDemo.csproj` 原本 `<ProjectReference Include="..\SharpDemo.Glue\SharpDemo.Glue.csproj" />`，但 `SharpDemo.Glue` 目录已被删（git status 显示 D）。还 target `net9.0`（其他都是 net10.0）。

**解决**：改成不引用 SharpDemo.Glue，target net10.0，`SharpDemo.dll` 引用指向 `..\..\Binaries\Managed\net10.0\SharpDemo.dll`。

## 4. 关键文件

| 文件 | 作用 |
|------|------|
| `Plugins/UnrealSharp/Managed/DotNetRuntime/inc/coreclrhost.h` | raw CoreCLR C API 头 |
| `Plugins/UnrealSharp/Source/ThirdParty/CoreClrSDK/` | Android CoreCLR 运行时 + BCL（从 `dotnet publish -r android-arm64 --self-contained` 填充） |
| `CoreClrSDK.Build.cs` | Android 无条件激活，BCL NonUFS + 项目 DLL UFS + APL |
| `CoreClrSDK_APL.xml` | 把 .so 拷进 APK `lib/arm64-v8a/`（**APL 注释不能含 `--`**） |
| `CSDotNetRuntimeHost_Android.cpp` | Android raw CoreCLR host |
| `BUILD_ANDROID.md` | 详细打包文档 |

## 5. 验证设备 logcat 关键行

成功时应有：
```
[CoreClr-Android] Runtime dir: /storage/.../files/Managed/Android
[CoreClr-Android] TPA built: N chars, M assemblies
[CoreClr-Android] coreclr_initialize => 0x0 (domain 1)
[CoreClr-Android] coreclr_create_delegate => 0x0, fn=0x...
UnrealSharp initialized successfully.
Discovered 1 load order manifests.
Successfully loaded plugin: 'ManagedSharpDemo' / 'UnrealSharp' / 'SharpDemo.RuntimeGlue' / 'SharpDemo'
```

## 6. GC 崩溃（Disregard for GC）— ✅ 已修复

**现象**（2026-06-19）：运行一段时间后 GC 触发，崩溃：
```
LogGarbage: Warning: Disregard for GC object CSManager /Engine/Transient.CSManager
  referencing CSManagedAssembly /Engine/Transient.CSManager:SharpDemo
  which is not part of root set
Fatal error: GarbageCollectionVerification.cpp:155
```

**根因**：`CSManager` 在 PostDefault 阶段创建于 transient package → 进 disregard-for-GC pool。
disregard 对象只能引用 root set / disregard / cluster 对象。`SharpDemo` assembly 在**运行时**
（native `Bind_UClass` 触发，disregard 池已关闭后）创建，无 `RF_MarkAsRootSet` → 不在 root set
也不是 disregard → 被 CSManager 引用违规。Win64 编辑器不崩是因为 `WITH_EDITOR` 关闭 GC 验证
（`GShouldVerifyGCAssumptions = !WITH_EDITOR`），latent bug 没暴露。

**修复**（`CSManager.cpp` LoadAssemblyByPath）：non-collectible assembly 创建时加
`RF_MarkAsRootSet`：
```cpp
const EObjectFlags AssemblyFlags = bIsCollectible ? RF_Public : (RF_Public | RF_MarkAsRootSet);
Assembly = NewObject<UCSManagedAssembly>(this, *AssemblyName, AssemblyFlags);
```
collectible 不加（保留编辑器热重载回收）。详见 `ANDROID_GC_CRASH_ANALYSIS.md`。

**验证**：app 稳定运行 7.5 分钟无崩溃（之前 638 帧/~10 秒崩），所有 assembly 加载成功。

## 7. 这一轮修复的 6 个真问题（按发现顺序）

1. **SDK 检测 bug → OOM**（`CSDotnetUtilties.cpp`）：PATH 标记 `Program Files\dotnet\` 带尾反斜杠
   匹配不到无尾斜杠条目 + `UnrealSharpCore.cpp` `StartupModule` 递归死循环 → cook OOM。
2. **MSBuildLocator 崩溃**：`UnrealSharp.Plugins.csproj` 无条件 PackageReference +
   `Main.cs` 顶层 using → Android 无 dotnet SDK 静态初始化抛异常。修：条件 PackageReference +
   using 包进 `#if WITH_EDITOR` + publish 加 `-p:DisableWithEditor=true`。
3. **Game glue 缺失**：`UnrealSharp.csproj` 编译 `UHT/Game/**/*.cs`，Editor build 只生成
   `UHT/Editor/`。必须先 `Build.bat SharpDemo Win64` (Game target) 生成 Game glue。
4. **UserAssemblyDirectory 路径不匹配**（`CSPathsUtilities.cpp`）：Android packaged 下指向
   `PersistentDownloadDir/Managed/Android`（提取目录），否则 `Discovered 0`。
5. **AssemblyDependencyResolver 不支持**（`PluginLoadContext.cs` + `Plugin.cs`）：Android
   raw-coreclr host 无 hostpolicy，ctor 抛 `PlatformNotSupportedException`。fallback 手动目录解析。
6. **Shared collectible context**（`CSManager.cpp`）：Android 下强制 non-collectible
   （`#if PLATFORM_ANDROID && !WITH_EDITOR`，`bCollectible=false`），避免共享依赖冲突。

