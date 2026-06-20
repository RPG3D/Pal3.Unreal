# Android GC 崩溃 — 静态分析 + 修复

> ✅ **已修复并验证（2026-06-19）**：方案 A 实施，app 稳定运行 7.5 分钟无 GC 崩溃
> （之前 638 帧/~10 秒崩）。所有 assembly 加载成功，无 `Disregard for GC` 违规。
>
> 日期：2026-06-19。

## 现象

Android packaged (Development) 运行一段时间后，GC 触发时崩溃：
```
LogGarbage: Warning: Disregard for GC object CSManager /Engine/Transient.CSManager
  referencing CSManagedAssembly /Engine/Transient.CSManager:SharpDemo
  which is not part of root set
Fatal error: GarbageCollectionVerification.cpp:155
  Encountered 1 object(s) breaking Disregard for GC assumptions.
```
即：`CSManager`（Disregard for GC 对象）引用了 `CSManagedAssembly:SharpDemo`（不在 root set），违反 GC 假设。

## 为什么 Win64 不崩、Android 崩

`GarbageCollectionVerification.cpp:36-45`:
```cpp
GShouldVerifyGCAssumptions          = !UE_BUILD_SHIPPING && !UE_BUILD_TEST && !WITH_EDITOR;
GShouldVerifyGCAssumptionsOnFullPurge = !UE_BUILD_SHIPPING && !WITH_EDITOR;
```
- **Win64 Editor**：`WITH_EDITOR=true` → 验证关闭 → 违规引用不报错（latent bug 没暴露）
- **Android Development packaged**：`WITH_EDITOR=false` + 非 Shipping → 验证开启 → 违规被检测 → Fatal

**结论：这个 GC 引用 bug 在 Win64 也存在，只是编辑器不验证所以没崩。** Android packaged build 启用验证暴露了它。

## Disregard for GC 判定（`UObjectArray.h:1234`）

```cpp
bool IsDisregardForGC(const UObjectBase* Object) const {
    return IsIndexDisregardForGC(Object->InternalIndex);
}
```
对象在 **permanent object array**（`InternalIndex <= ObjLastNonGCIndex`）就算 disregard。
permanent array = 在 `CloseDisregardForGC()` 之前创建的所有对象。

`CloseDisregardForGC()`（`UObjectArray.cpp:146`，由 `LaunchEngineLoop.cpp:3863` 在 `LoadStartupModules()` **之后**调用）：
- 遍历所有对象，`IsSafeForRootSet()` 的 `AddToRoot()`，已 root 的 `RemoveFromRoot()`
- `ObjFirstGCIndex = ObjLastNonGCIndex + 1` → 此后创建的对象走 GC

## 违规判定（`GarbageCollectionVerification.cpp:110-120`）

Disregard 对象引用 `Object` 时，必须满足任一：
```cpp
Object->IsRooted()                                    // 在 root set
|| GUObjectArray.IsDisregardForGC(Object)             // 也 disregard
|| GetOwnerIndex() > 0                                // 是 cluster 成员 (ClusterRootIndex)
|| HasAnyFlags(EInternalObjectFlags::ClusterRoot)     // cluster root
```
否则：Warning + NumErrors++ → 最终 Fatal。

## 根因链

1. **`UCSManager::Get()`**（`CSManager.h:30`）：
   ```cpp
   Instance = NewObject<UCSManager>(GetTransientPackage(), "CSManager", RF_Public | RF_MarkAsRootSet);
   ```
   - 创建于 `GetTransientPackage()`（transient package 本身 disregard）
   - `UnrealSharpCore` LoadingPhase = `PostDefault` → `StartupModule` 在 `LoadStartupModules()` 内运行 → 在 `CloseDisregardForGC()` **之前**
   - → `CSManager` 进 permanent array → **disregard for GC**
   - （`RF_MarkAsRootSet` 让它也在 root set，但 transient package + 早期创建使它 disregard）

2. **`UCSManagedAssembly` 创建**（`CSManager.cpp:229`）：
   ```cpp
   Assembly = NewObject<UCSManagedAssembly>(this, *AssemblyName);  // outer = CSManager, 无 RF_MarkAsRootSet
   ```
   - **没有 `RF_MarkAsRootSet`**，不是 cluster root/member

3. **`SharpDemo` assembly 在运行时创建**（关键）：
   - `InitialAssemblyLoad()`（StartupModule 内，disregard 池还开着）只加载 manifest 里的 `ManagedSharpDemo` + `SharpDemo.RuntimeGlue`
   - `SharpDemo` assembly 是**后续运行时**（native `Bind_UClass` / `FindOrLoadAssembly("SharpDemo")`）加载的——日志时序证实：manifest 加载在启动早期，`SharpDemo` plugin 在 ~1 秒后加载
   - 此时 `CloseDisregardForGC()` 已执行 → `SharpDemo` 的 `CSManagedAssembly` `InternalIndex > ObjLastNonGCIndex` → **不是 disregard**，也不在 root set，也不是 cluster
   - 被 disregard 的 `CSManager`（`Assemblies` map 持有它）引用 → 违规 → 崩溃

   注：`ManagedSharpDemo`/`SharpDemo.RuntimeGlue` 在 disregard 池关闭前创建 → 它们的 `CSManagedAssembly` 进 permanent array → disregard → 不违规。只有**运行时**加载的 `SharpDemo` 违规。

## 为什么 `Assemblies` 是 UPROPERTY 却出问题

`CSManager` 的 `TMap<FName, TObjectPtr<UCSManagedAssembly>> Assemblies` 是 `UPROPERTY(Transient)`。
但 `CSManager` 是 **disregard** 对象——disregard 对象的 UPROPERTY 引用**不被 GC 常规扫描**
（它们在 permanent pool，GC 跳过）。所以 GC 验证专门检查：disregard 对象引用的目标必须
合法（root set / disregard / cluster）。`SharpDemo` 的 `CSManagedAssembly` 不满足任何一条。

## 修复方向（待实施，未动手）

核心目标：让 disregard 的 `CSManager` 引用的所有 `CSManagedAssembly` 都满足 GC 假设。
几个候选方案：

### 方案 A：`CSManagedAssembly` 创建时 `RF_MarkAsRootSet`（最小改动）
`CSManager.cpp:229` 改为：
```cpp
Assembly = NewObject<UCSManagedAssembly>(this, *AssemblyName, RF_Public | RF_MarkAsRootSet);
```
- 运行时创建的 assembly 进 root set → `IsRooted()` 满足 → 不违规
- 代价：assembly 永不 GC（但 UnrealSharp 本来就长期持有它们，符合预期）
- 风险：需要在卸载时 `RemoveFromRoot`（`UnloadAssembly` 路径），否则泄漏

### 方案 B：`CSManager` 不进 disregard 池
让 `CSManager` 在 `CloseDisregardForGC()` **之后**创建（延迟初始化），或不用 `GetTransientPackage()`
作 outer。这样 CSManager 不是 disregard，GC 正常扫描它的 `Assemblies` 引用。
- 风险：改动 `Get()` 时机/位置，可能影响其他逻辑

### 方案 C：把 `Assemblies` 改为 `TStrongObjectPtr` / `FGCObject`
不用 UPROPERTY 持有 assembly，改用 `TStrongObjectPtr<UCSManagedAssembly>`（自动 root）
或 `FGCObject` 手动 `AddReferencedObjects`。
- 最干净但改动面大

### 方案 D（临时）：关闭 GC 验证
`gc.VerifyGCAssumptions=0` 等控制台变量 —— **只是绕过验证，不修 bug**，不推荐（Win64 也只是没验证）。

## 推荐

**方案 A**（`RF_MarkAsRootSet`）最小、最直接，符合 assembly 长期持有的语义。
**已实施并验证。**

### 实施的修改（`CSManager.cpp` LoadAssemblyByPath）

```cpp
// non-collectible assembly 进 root set；collectible 不加（保留编辑器热重载回收）
const EObjectFlags AssemblyFlags = bIsCollectible ? RF_Public : (RF_Public | RF_MarkAsRootSet);
Assembly = NewObject<UCSManagedAssembly>(this, *AssemblyName, AssemblyFlags);
```

为什么安全：
- Android packaged（强制 `bIsCollectible=false`）：运行时创建的 assembly 进 root set → `IsRooted()` 满足 → 不违规
- 启动时创建的 assembly（disregard 池关闭前）：带 `RF_MarkAsRootSet`，但 `CloseDisregardForGC` 会对已 root 对象 `RemoveFromRoot` → 剥离 root，仍靠 permanent pool（disregard）→ 合法
- Win64 编辑器 collectible：不加 root → 可热重载回收（且 `WITH_EDITOR` 关闭 GC 验证，不崩）
- `CSManagedAssembly` 在 Android 下几乎无 UPROPERTY 引用其他 UObject（`DependentAssemblies` 只在 `WITH_EDITORONLY_DATA`），不会递归违规
- `UnloadAssembly` 对 non-collectible 直接 return（不卸载），无需 `RemoveFromRoot`；collectible 不加 root，卸载时不需清理

### 验证结果

- app 稳定运行 7.5 分钟（之前 638 帧/~10 秒崩）
- 所有 4 个 assembly 加载成功（ManagedSharpDemo / UnrealSharp / SharpDemo.RuntimeGlue / SharpDemo）
- 无 `Disregard for GC` 警告，无 Fatal/SIGSEGV
- 触屏操作角色 + 持续运行，UObject GC full purge 触发验证，无违规

## 待确认

- `CSManagedAssembly` 卸载路径（`UnloadAssembly`）是否清理 root set
- 是否有其他 disregard 对象引用运行时创建的 assembly（不止 CSManager）
- `ManagedSharpDemo`/`SharpDemo.RuntimeGlue`（disregard 池关闭前创建）是否真的不违规——
  若 disregard 池关闭时它们被 `IsSafeForRootSet()` 判定，需确认它们的状态

## 相关文件（只读分析，未修改）

- `Plugins/UnrealSharp/Source/UnrealSharpCore/Public/CSManager.h` — `Get()` 创建 CSManager（transient + MarkAsRootSet）、`Assemblies` UPROPERTY
- `Plugins/UnrealSharp/Source/UnrealSharpCore/Private/CSManager.cpp:229` — `NewObject<UCSManagedAssembly>(this, ...)` 无 MarkAsRootSet
- `Plugins/UnrealSharp/Source/UnrealSharpCore/Public/CSManagedAssembly.h` — UCLASS(Transient)，无 AddReferencedObjects
- 引擎：`GarbageCollectionVerification.cpp:110-155`（违规判定）、`UObjectArray.cpp:146`（CloseDisregardForGC）
