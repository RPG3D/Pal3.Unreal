# UE5.9 升级迁移记录（2026-11-14）

引擎：`D:/Code/UE5`（Build.version 5.9.0，源码版；GUID `{A8AD6A96-449D-7D70-175F-DBBAEA8C6118}` 不变）。
目标：恢复编辑器编译/启动 + Android APK 打包。以下均为实测结论。

---

## 1. 编辑器（Win64）

### 1.1 UnrealSharp ManagedGlue 的 UHT API 变化
UE5.9 的 `EpicGames.UHT` 移除了 `UhtObject.AlternateObject`（源码里已无该属性），接口配套类拆成：
- `UhtClass.InterfaceClass`（native IInterface → UInterface）
- `UhtClass.NativeInterfaceClass`（UInterface → native IInterface）

处理：`Utilities/ClassUtilities.cs` 新增 `GetAlternateClass()`，用反射兼容两代 API：
先查 `AlternateObject`（5.8），否则依次尝试 `NativeInterfaceClass` / `InterfaceClass`（getter 对非法类型会抛异常，已捕获）。
`InterfaceExporter` / `NameMapper` / `GetInterfaceAlternateClass` 全部走该 helper。

### 1.2 生成代码与新 UHT 导出冲突
- **CS0111 重复成员**：5.9 UHT 开始导出 `AActor/UActorComponent.AddReplicatedSubObject`、
  `RemoveReplicatedSubObject`、`IsReplicatedSubObjectRegistered`，与 `Managed/UnrealSharp/.../Extensions/Engine/Actor.cs`、
  `ActorComponent.cs` 里手写的同名包装重复 → 删除手写实现（生成绑定已覆盖）。
- **枚举值前缀**：5.9 导出的枚举保留引擎前缀（`ENetMode.NM_Standalone/…`），
  手写 `World.cs` 里 `ENetMode.Standalone/Client/ListenServer/DedicatedServer` 全部改 `NM_*`。
- `JsonSchemaGeneratorFakeClass`（JsonUtilitiesEditor 的测试类）在 5.9 触发
  `NotImplementedException`（新属性类型无 translator），导致 UHT ICE 使整个构建失败。
  处理：`PackageExporter` 对每个类型的导出加 try/catch，把失败类型写
  `Saved/Logs/unrealsharp-glue.log` 后跳过，保证其它类型继续导出。

### 1.3 UE_LOGFMT 格式串静态检查（C2338）
UE5.9 对 `UE_LOGFMT` / `UE_LOG` 做编译期格式校验：
- `UE_LOGFMT(..., "…%s", ptr)` 不再接受 printf 占位符 → 改 `UE_LOG(..., TEXT("…{Field}"))` 或 printf `UE_LOG`。
- `UE_LOG` 的 `%s` 要求 `TCHAR*`；`const char*`（UTF8）必须 `UTF8_TO_TCHAR(...)`。
修改点：`UnrealSharpCore/Private/Binds/Bind_UCoreUObject.cpp`。

### 1.4 Cascade 从 Engine 移到插件
UE5.9 把 Cascade 拆到 `Engine/Plugins/FX/Cascade`（模块 `CascadeRuntime` 等），并移除了
`UGameplayStatics.SpawnEmitterAtLocation` 等 UFUNCTION（引擎改为函数指针回调）。
- 项目没用到旧粒子系统：在 `SharpDemo.uproject` 中禁用 `Cascade` 与 `CascadeToNiagaraConverter`
  （后者 EnabledByDefault 且依赖 Cascade，会把它强制打开）。
- 未禁用时的症状：生成的 `Engine` glue 仍绑定 `SpawnEmitterAtLocation`，启动时
  `Failed to get NativeFunction … SpawnEmitterAtLocation` → `Assertion failed: NativeFunction`（Bind_UFunction.cpp）。

### 1.5 构建缓存/SDK 混用坑
- `Intermediate/Build/BuildRules/SharpDemoModuleRules.dll` 若由 **.NET 11 SDK** 编译，会引用
  `Microsoft.Extensions.Logging.Abstractions 11.0.0`；用引擎自带 .NET 10 运行 UBT 时加载失败
  （`Unable to instantiate module 'DotNetRuntimeSDK'`）。处理：删除该缓存，统一用
  `Engine/Binaries/ThirdParty/DotNet/10.0/win-x64/dotnet.exe` 运行 UBT。
- 五.9 下 UHT 只在头文件变化时执行 exporter。若 glue 缺失/陈旧（例如删除了
  `Plugins/UnrealSharp/Intermediate/UnrealSharp/UHT`），touch 一个项目头文件（如
  `Source/SharpDemo/Pal3DebugOverlay.h`）即可触发 UHT + 全部 exporter 重新生成并重编
  `UnrealSharp.dll`（引擎绑定，约 54MB）。
- 判定 glue 是否过期：`grep` 已删除的 UFUNCTION 名（如 `SpawnEmitterAtLocation`）是否仍出现在
  `Plugins/UnrealSharp/Binaries/Managed/net10.0/UnrealSharp.dll`。

### 1.6 验证
- `UnrealEditor.exe SharpDemo.uproject -log` 正常启动，8 个托管程序集全部加载，无断言。
- PIE 启动：`[Pal3Boot] Session state reset` → `Full boot starting` → `[Pal3] Game started`，
  `[Pal3Driver] actors=…` 心跳正常。

---

## 2. Android APK

### 2.1 工具链
- UE5.9 `AndroidToolChain.MinimumBestVersionString = 28.0.3` → **需要 NDK r28**。
  通过 sdkmanager 安装：`ndk;28.0.13004108`（`%LOCALAPPDATA%/Android/Sdk/ndk/28.0.13004108`），
  `BuildAndroid.bat` 的 `NDKROOT` 已同步。
- JDK 21（PATH 中的 Microsoft OpenJDK 21.0.12）可用；SDK platform 36 / build-tools 已存在。
- 打包仍走 `BuildAndroid.bat Development`（自动检测 `PackageProjectMono`，PATH 指向
  `C:\Program Files\dotnet`，托管 net10）。

### 2.2 引擎源码在 NDK r28 下的编译错误
```
ApplicationCore/Private/Android/AndroidInputInterface.cpp(1561,14):
error: declaration shadows a local variable [-Werror,-Wshadow]
```
`FScopeLock Lock(&TouchInputCriticalSection);` 的 `Lock` 与外层同名变量冲突。
本地引擎补丁（源码版，未提交上游）：
- `FScopeLock Lock` → `FScopeLock ScopeLock`
- 同文件另一处 `GenericApplication* App` → `GenericApplication* OwningApp`（同类警告）

### 2.3 构建结果
```
APK:  Binaries\Android\SharpDemo-arm64.apk
Size: 239,733,973 bytes (~229 MB)
AutomationTool exiting with ExitCode=0 (Success)
```
耗时参考（12 核）：Win64 Game target 全量 ~1035 actions ≈ 25 min；Android arm64 ~1942 actions
≈ 20 min；cook+gradle ≈ 5–8 min。增量重跑（仅改一个引擎 cpp）≈ 6 min。

### 2.4 命令
```bat
:: 编辑器 C++（必须用引擎自带 .NET 10）
D:\Code\UE5\Engine\Binaries\ThirdParty\DotNet\10.0\win-x64\dotnet.exe ^
  D:\Code\UE5\Engine\Binaries\DotNET\UnrealBuildTool\UnrealBuildTool.dll ^
  SharpDemoEditor Win64 Development -Project=D:\UEProject\SharpDemo\SharpDemo.uproject -WaitMutex

:: APK（内部依次：Game target → PackageProjectMono → Editor → BuildCookRun Android）
D:\UEProject\SharpDemo\BuildAndroid.bat Development
```

---

## 3. 构建环境

- UHT 5.9 兼容、生成器诊断和手写绑定清理由 `Plugins/UnrealSharp` 子模块提供。
- 引擎源码补丁（`D:\Code\UE5`）是本地修改，不属于本项目；升级引擎前需重新套用或等上游修复。
- UHT 生成的 `Script/*.csproj` 引用（`RuntimeBinkVideo`）与 `GameplayTags.cs` 会被 UnrealSharp
  自动改写，属于正常生成物。
- 备选：若后续升级到已修复该 shadow 警告的 5.9 正式版，可还原 `AndroidInputInterface.cpp`。
