# 从 PAK 内存加载托管 DLL（external_assembly_probe）

> 硬核话题：让 CoreCLR 直接从 UE PAK/NonUFS 内存加载托管 assembly，**不解压到磁盘**。
> 当前状态：**未完成，留待后续推进**。当前用 xxHash 按需解压方案（见 `ANDROID_BUILD_NOTES.md`）。
> 独立研究建议在 `AndroidClrDemo` 上验证机制（工程精简，迭代快），再移植到插件。

## 背景与目标

当前 Android CoreCLR host 把 BCL + 项目 DLL 从 PAK/NonUFS 解压到 `ProjectSavedDir/Managed/Android/`，
CoreCLR 从磁盘加载（TPA 指向磁盘路径）。xxHash 优化后只在文件变化时解压，但仍需磁盘缓存。

**目标**：用 CoreCLR 的 `external_assembly_probe` 回调，让 CoreCLR 直接从 UE I/O（PAK/UFS/NonUFS）
读 assembly 字节到内存加载，**完全不解压**。这是 CoreCLR 等价于 Mono `mono_install_assembly_preload_hook` 的机制。

## 机制（已验证可行 ✅）

`coreclrhost.h` + `host_runtime_contract.h`（dotnet/runtime `src/native/corehost/`）：

1. `coreclr_initialize` 接受 property `"HOST_RUNTIME_CONTRACT"`，值是 `host_runtime_contract` 结构体指针的十六进制字符串
2. 结构体含 `external_assembly_probe(path, &data_start, &size)` 回调
3. CoreCLR 加载 assembly 时（`assemblyspec.cpp` `PEImage::OpenImage` → `AssemblyProbeExtension::Probe`）调 probe
4. probe 返回内存指针 + 大小 → CoreCLR 从内存加载，**不走文件系统**

已验证：
- ✅ contract 机制工作（`HOST_RUNTIME_CONTRACT` property 被识别，size=48 正确）
- ✅ `System.Private.CoreLib.dll` 从 PAK/NonUFS 内存加载成功（17MB，无解压）
- ✅ probe 回调用 `FFileHelper::LoadFileToArray`（UE I/O，PAK 感知）读字节返回

## 当前卡点 ❌

`coreclr_initialize` 在 CoreLib 加载后返回 `E_INVALIDARG (0x80070057)`，probe 只被调 1 次（CoreLib），
后续 BCL 没走 probe。

**根因推测**：CoreCLR 加载非 CoreLib 的 BCL 时，按 TPA 路径解析。TPA 路径是 UE 相对虚拟路径
（`../../../SharpDemo/.../runtime/System.Runtime.dll`），CoreCLR 不认（要 OS 绝对路径 stat）→ 解析失败
→ E_INVALIDARG，没到 probe 那步。

证据：
- probe 只调 1 次（CoreLib 用短名 `System.Private.CoreLib.dll`，CoreCLR 特殊处理）
- TPA[0]=CoreLib（已修排序），TPA[1..]=相对虚拟路径
- CoreCLR error writer 无额外输出（在 assembly 加载前失败）

## 已尝试

1. contract 指针串格式（`0x%llx`）—— OK，contract 被识别
2. exePath 用 `GAPKFilename`（APK 真实路径）—— OK，PAL 初始化过
3. TPA 排序 CoreLib 最前 —— OK
4. TPA 路径 `ConvertRelativePathToFull` —— 仍是 `../../../` 形式（UE 内部路径，非 OS 绝对）

## 后续推进方向（待做）

1. **TPA 用 OS 绝对路径**：Android 上 NonUFS BCL 文件的真实 OS 路径。用
   `FFileManagerGeneric::ConvertToAbsolutePathForExternalAppForRead` 转换。但 PAK 内项目 DLL 没有 OS
   路径——可能要混合（BCL OS 路径 + 项目 DLL 虚拟路径，或项目 DLL 也 NonUFS）。
2. **读 monodroid `monodroid-coreclr.c`** 的 probe 实现，看它 TPA 用什么路径格式（最有价值的参考）
3. **`APP_PATHS` property**：corerun 设了 APP_PATHS（probe 方案没设），可能 CoreCLR 要它定位 BCL
4. **`PluginLoadContext` 内存加载**：即使 probe 拦截 BCL，项目 DLL 经 `PluginLoadContext.File.Open`
   仍需磁盘。彻底内存加载需 C++ 导出"读字节"全局 C 函数给 C# P/Invoke（`LoadFromStream`），见
   `PluginLoadContext` 的 `File.Open` → native 回调。

## 关键文件（probe 方案，当前已回退未启用）

- `host_runtime_contract.h`（`Managed/DotNetRuntime/inc/`）—— contract 结构（probe 方案回退时已删，
  重新推进时从 dotnet/runtime `src/native/corehost/host_runtime_contract.h` 拷回）
- `CSDotNetRuntimeHost_Android.cpp` —— probe 回调 + coreclr_initialize（当前是解压版，probe 版见 git
  历史 / patch `UnrealSharp-AndroidCoreClr-20260619-preopt` 之后的 probe 尝试）
- CoreCLR 源：`src/coreclr/vm/assemblyprobeextension.cpp`（probe 调用）、`assemblyspec.cpp:433`
  （LoadAssembly 调 probe）、`dlls/mscoree/exports.cpp:170`（contract 解析）

## 相关

- `ANDROID_BUILD_NOTES.md` —— 当前 xxHash 解压方案（probe 未启用时的可用方案）
- `AndroidClrDemo`（`E:\Code\CoreClrDemo\AndroidClrDemo`）—— 独立验证 probe 的精简工程
