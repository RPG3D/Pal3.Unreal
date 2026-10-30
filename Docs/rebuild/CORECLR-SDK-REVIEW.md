# CoreCLR SDK 改动逐处审查（2026-10-29）

范围：`Plugins/UnrealSharp/Source/ThirdParty/CoreClrSDK`（submodule，`main`）+ 与之配套的
UnrealSharp 宿主/生成器改动。目的：每一处 diff 都能说出「为什么改、证据是什么、不改会怎样」。

---

## 0. 版本绑定规则（本次确立）

运行时分支由 **backend** 决定，不随平台漂移：

| backend | dotnet/runtime 分支 | TFM（SDK 载荷） | 托管程序集 TFM |
| --- | --- | --- | --- |
| Mono | `release/10.0` | `net10.0` | `net10.0` |
| CoreCLR | `release/11.0` | `net11.0` | `net10.0` |

理由（两条硬约束，互斥，所以必须一对二绑定）：

1. **.NET 11 的 coreclr 树里没有 mono**：`build.sh mono+libs` 在 11 上直接不可编译，Mono 只能停在 10。
2. **.NET 10 的 iOS CoreCLR 默认不开解释器**：iOS 无 JIT，不开解释器等于没有可用的执行引擎；要启用得改 SDK 内部（agent 不可支撑），所以 iOS CoreCLR 只能走 11。

托管侧不跟着升：11 运行时可以承载 net10 托管程序集（IOSClrDemo 实测 8/8，见 §4.2），
因此 `DotNetRuntime.version` 描述的是 **CLR 载荷**，不是托管 TFM。

唯一登记在案的例外：**Android CoreCLR 仍是 10.0.12**（四组合真机验证过的那一版）。它在
`DotNetRuntime.version` 里以 `*_ANDROID` 三行显式存在，删掉这三行即完成迁移；`CopySDKFromSrc.sh`
与 `CoreClrSDK.Build.cs` 都会核对「载荷声明的 major == 版本文件给该平台解析出的 major」，
不一致直接失败，不允许静默混用。

---

## 1. 已在 main 历史里的改动（本轮之前）

| 提交 | 改动 | 为什么（证据） |
| --- | --- | --- |
| `0e7eb88` | 后端门控：仅当 `UnrealSharpRuntimeSelection.ShouldUseMono(Target)==false` 时才为 Android/iOS stage BCL + link CoreCLR | 两个 backend 都无条件 stage 时，`Content/Managed/<Platform>` 会被两套 BCL 互相覆盖，且两个运行时同时链进二进制。门控后又需要 `dotnet11-ios` 侧把 engine ini 注册成 `ExternalDependencies`（MonoSDK `30ca4cc`），否则 UBT 的 makefile 缓存不会因 ini 变化而失效 |
| `fc86ed2` | iOS：解释器专用 SDK、framework `install_name`、引擎与 shim 分层 | iOS 产物里 `libcoreclr.dylib` 的 id 是 `@rpath/libcoreclr.dylib`，直接放进 `Frameworks/` 时 dyld 去 `SharpDemo.app/Frameworks/libcoreclr.dylib` 找 → `Library not loaded`。CoreCLR 又从**自身镜像目录** dlopen 引擎（`vm/codeman.cpp`），所以 `libclrjit/libclrinterpreter` 必须与 framework 二进制同级，只有 `System.*` shim 留在 `CoreCLR.framework/Frameworks/` |
| `ddcb1fb` | 预签名 `CoreCLR.framework` 内嵌 dylib | 见 §4.1：UE 只签 app 与框架本身，嵌套 dylib 一个都没签 |
| `2e60e8d` | CI 脚本避免 bash 3.2 + `set -u` 下空数组展开 | macOS 自带 bash 3.2，`"${arr[@]}"` 在空数组时会以 unbound variable 中止 |
| `dd31270` | 删除 NuGet fetch 脚本 | 用户明确要求：不得用 NuGet 取微软官方运行时包 |
| `9dd7d87` | `FetchCoreClrSDK.sh` 只走 GitHub Releases | 同上。实现上带 `--tag`、`curl -C -` 断点续传、4 次退避重试、载荷完整性校验，并按 `<Platform>/<arch>` 一对一安装 |
| `af5a066` | 运行时钉 `v10.0.12` | 与 Win11 上已验证过的 `10.0.12-dev@15ce0ebb4` 对齐；11.0 rc.1 在本机 iOS 上 `coreclr_initialize` 直接 0x80070002 |
| `e6b3be0` | 载荷路径补 `<arch>` 层（`Android/arm64`、`IOS/arm64`、`IOSSimulator/arm64`） | `Build.cs` 还在探旧的扁平 `Android/lib`，`File.Exists` 为假 → 库根本不参与链接，B/D 组合在链接期报 `undefined symbol: coreclr_initialize/shutdown/create_delegate`。Win11 上之所以"能过"，是因为当时被删掉的 NuGet 脚本写了扁平布局 |
| `54eaeaa` | iOS 补丁在 10 分支无匹配时跳过；有 ninja 时钉 `-ninja` | 补丁用的 pattern 只存在于 11 的 `src/native/libs/*/CMakeLists.txt`；`grep` 无匹配返回 1，在 `set -euo pipefail` 下会整段静默退出。generator 冲突来自多次构建留下的不同 CMake generator |
| `7cbc428` | **反面案例**：`Merge backend-gate into main` 把 `CoreClrSDK.Build.cs` 整体重写（-508/+84），冲突解决时丢掉了 `ddcb1fb` 的预签名块 | `git merge-base --is-ancestor ddcb1fb HEAD` 为真，但 `git diff ddcb1fb HEAD -- CoreClrSDK.Build.cs` 显示 `SignEmbeddedFrameworkZip / FindCodeSigningIdentity / GetConfiguredSigningTeam / codesign 调用` 全部被删。这是 iOS CoreCLR 真机挂掉的直接来源，也是本次重新落地的内容 |

---

## 2. 工作区未提交的改动（CoreClrSDK）

### 2.1 `CoreClrSDK.Build.cs` — 内嵌框架预签名（重落 `ddcb1fb`）

- **现象**：`Packaged/IOS-Mono-CoreCLR/SharpDemo.app/Frameworks/CoreCLR.framework/Frameworks/*.dylib`
  与 `libclrjit.dylib`、`libclrinterpreter.dylib` 全部 `code object is not signed at all`；而同一台 iPad
  上 IOSClrDemo 的框架（脚本里逐个 `codesign`）全部 signed。
- **后果链**：iOS 拒绝 dlopen 未签名 dylib（EPERM）→ 第一个 P/Invoke（`GlobalizationMode` 初始化
  读环境变量 → `libSystem.Native`）抛 `DllNotFoundException` → 构造该异常需要 SR →
  `ResourceManager` → `CultureInfo` → 又落到 Globalization → 递归 `Environment.FailFast`（signal 6）。
  托管栈看起来停在 `AssemblyDependencyResolver..ctor`（解释器下 `RequiresDynamicCode` 守卫）其实只是
  第一个受害者。
- **做法**：UBT 期 unzip → `codesign -f -s <identity> --timestamp=none` 逐个签 `Frameworks/*.dylib` +
  两个引擎 + 框架二进制 → 原生 `zip -X -y` 回包（保住 unix 权限位与符号链接，.NET `ZipArchive` 会压平）。
  签完再由 UE 封框架，顺序正确。
- **跳过缓存双因子**：`.framework_sign_cache.txt` 存 `<zip sha256> <identity sha1>`。只按内容哈希会
  在换证书/换团队/手动↔自动签名后复用旧签名（用户去年就提过这个场景）；`ddcb1fb` 当初也是双因子，
  本次沿用它而不是 MonoSDK 的单因子。
- **签名身份选择**：优先 `Config/IOS/IOSEngine.ini` 的 `CodeSigningTeam` 对应证书（本机 `8NDXS74A32`），
  否则第一个有效身份；跳过 `CSSMERR_`（过期/吊销）与非 40-hex token。团队不一致时 dlopen 依然失败，
  所以不能随便取第一个。
- **失败降级**：任何异常只 `LogWarning` 并继续，不阻断构建（与 MonoSDK 行为一致）。
- **验证**：负向测试「把载荷 VERSION.txt 改成 10」时 UBT 报 `RulesError`（§2.3 的守卫）；正向则
  `already signed; skipping`，包内 8 个 Mach-O 全部 signed，真机进图。

### 2.2 `CoreClrSDK.Build.cs` — 载荷/版本一致性与 `sdkRoot` 字段

- `string sdkRoot = ModuleDirectory;` 原来是构造函数局部变量，新增 helper 用不到 → 提升为字段。
- 新增 `VerifyPayloadRuntimeMajor()`：读 `DotNetRuntime.version` 解析 `<KEY>_<PLATFORM>`→`<KEY>`，
  与载荷 `VERSION.txt` 的 `Runtime: <major>.<ver> (<tfm>)` 比对，不一致抛 `BuildException`。
  存在但不匹配才失败；老载荷没有该行时只告警。
- 理由：把 `release/10.0` 的产物拷进 iOS 槽位，链接完全正常，只在设备上才炸（解释器缺失 / CoreLib 不匹配）。

### 2.6 `MakeCoreClrFramework.sh` — 框架不再携带 CoreLib（2026-10-30 收回）

- 昨天为绕 `0x80070002` 把 `System.Private.CoreLib.dll` 拷进 `CoreCLR.framework/` 根；现在改为**由
  PAK 提供 + 宿主 probe 交付**，框架里不再有第二份副本（zip 已用脚本重建并核验：CoreLib 0 个）。
- 理由（用户定调）：CoreLib 只有一份来源、跟其它 BCL 一起 UFS 打包 → 可热更、可解释；框架副本既占
  1.7MB 又制造"两份 CoreLib 谁在跑"的歧义。**刻意不保留兜底**：probe 不生效时运行时会去框架找、找不到
  并 `0x80070002` 失败，问题立刻暴露，而不是悄悄跑旧副本。
- 真机验证（iPad，2026-10-30）：`external_assembly_probe: loaded SPCL at .../Saved/Managed/IOS/...` →
  `coreclr_initialize => 0x0` → `ScriptGameMode_C`，无 `FailFast/DllNotFound`。

### 2.7 Android CoreCLR 升到 11（2026-10-30）

- 载荷：复用留档 `Android/arm64.v005-net11.bak`（`release/11.0` @ `3beced8c`，`libcoreclr.so` 内
  `11.0.0-dev`，181 BCL + 完整 native 集），未从源码重建；10.0.12 的旧载荷备份为
  `Android/arm64.v006-net10.bak` 以便回滚。
- **`*_ANDROID` 例外三行已删除**，`DotNetRuntime.version` 现在只有一组 11 的值，构建期守卫
  （`VerifyPayloadRuntimeMajor`）随之按 11 校验。
- 遇到并解决的真机问题：11 的 Android CLR 不再接受"CoreLib 由 TPA 提供"，`coreclr_initialize` 直接
  `0x80070002`，日志给出确切路径 `<APK lib/arm64-v8a>/System.Private.CoreLib.dll`。把 CoreLib 放进
  载荷 `lib/` 无效——Gradle 会把非 `.so` 从 `lib/arm64-v8a/` 过滤掉（已用 `unzip -l` 核实）。最终方案见 §2.8：
  宿主 probe。
- 真机验证（小米 24091RPADC）：组合 B（编辑器 Mono × Target CoreCLR）与 D（编辑器 CoreCLR ×
  Target CoreCLR）均 `coreclr_initialize => 0x0` + `ScriptGameMode_C`。

### 2.8 宿主：两个移动平台共用一份 CoreLib probe（2026-10-30）

- 位置：`CSDotNetRuntimeHost_Mobile.cpp`，`#if PLATFORM_ANDROID || PLATFORM_IOS`，契约在平台分支之前
  构造，两端属性表都带 `HOST_RUNTIME_CONTRACT`。
- 依据：`vm/appdomain.cpp` 里 `m_BaseLibrary = m_SystemDirectory + g_pwBaseLibrary`，CoreLib 是唯一
  不走 TPA 的程序集；移动端的模块目录一个在只读 APK lib 里、一个在框架里，都不是我们想要的那份。
- 为什么必须 probe 而不是塞文件：Android 侧 Gradle 过滤非 `.so`；iOS 侧塞框架等于制造第二份副本
  （见 §2.6）。
- 类型注意：probe 签名必须用 `int64_t*`，用 `int64*` 在 Android（`long long`）上会与头文件
  `bool (*)(const char*, void**, int64_t*)` 不兼容，编译期直接报错（已踩）。
- **契约结构体必须 `static`**（review 发现并已修）：`exports.cpp` 里
  `*hostContract = hostContractLocal` 保存的是**指针**，运行时在 `coreclr_initialize` 返回后的任意
  一次程序集绑定时都会从该指针读 probe。栈局部变量的地址一旦传出，函数返回即悬垂——今天几轮设备
  测试没炸只是因为 probe 恰好都在 init 期间触发（CoreLib 只绑一次）。与早前 iOS 版的
  `static GHostContract` 对齐。
- Android 属性表补 `RUNTIME_IDENTIFIER=android-arm64`（2026-10-31，与 iOS 对称；真机 B 组复验通过）。

  这条守卫把它提前到构建期。

### 2.3 `DotNetRuntime.version`

- 语义从「按平台」改成「按 backend」：默认三行 = CoreCLR ⇒ 11 / net11.0 / 11.0.0。
- `*_ANDROID` 三行是**显式登记的例外**（Android CLR 仍在 10.0.12），注释写明关闭步骤：
  用 release/11.0 worktree 构建 Android 载荷 → 重跑 Android 组合 → 删除这三行。
- `BUILD_SUBSET` 未变（`clr.runtime+clr.corelib+clr.nativecorelib+libs.native+libs.sfx`），
  因为它是「SDK 实际消费的产物」而不是版本号；`clr.corelib` 是纯 IL CoreLib（解释器要求），
  `clr.nativecorelib` 是 runtime pack 里 R2R CoreLib 的输入。

### 2.4 `CopySDKFromSrc.sh`

- `resolve_version_key()`：先 `<KEY>_<PLATFORM>` 再 `<KEY>`（Android 例外需要）。
- 新增**源分支守卫**：从 `$DOTNET_SRC/eng/Versions.props` 读 `<MajorVersion>`，与解析出的
  `DOTNET_MAJOR` 不一致直接报错退出。
- `VERSION.txt` 增加 `Runtime: <major>.<version> (<tfm>)` 行，供 §2.2 的构建期守卫消费。

### 2.5 `MakeCoreClrFramework.sh` — CoreLib 放进框架根

- CoreCLR 用**自身镜像目录**推导 system assembly 路径（`vm/appdomain.cpp`：`GetClrModuleDirectory()`
  + `System.Private.CoreLib.dll`），不看 TPA / `APP_CONTEXT_*`。宿主可以用
  `external_assembly_probe` 契约钩子拦截，但我们的移动宿主刻意不用契约（与 IOSClrDemo 一致），
  所以框架自己必须带这个文件。
- 之前那条"CORELIB 找不到 → `coreclr_initialize` 0x80070002"就是这一条。
- 注：早先那版注释写"probe 不被咨询"是错的（probe 确实生效过），已按实测改正。

---

## 3. 关联的 UnrealSharp 侧改动（配套项）

| 位置 | 改动 | 依据 |
| --- | --- | --- |
| `CSDotNetRuntimeHost_Mobile.cpp` | iOS 绝对路径重建 `<Documents>/<Project>/Saved/Managed/IOS` | iOS 的 `ConvertToAbsolutePathForExternalAppForRead` 不会把 `../../../` 展开（Android 会，见 `AndroidPlatformFile.cpp:159`），TPA 必须是真实路径 |
| 同上 | `RUNTIME_IDENTIFIER` 按 `TARGET_OS_SIMULATOR` 区分 | 原值写死 `iossimulator-arm64`，真机 RID 错误 |
| 同上 | `NATIVE_DLL_SEARCH_DIRECTORIES` = `<bundle>/Frameworks/CoreCLR.framework/Frameworks` | 没有它，第一个 P/Invoke 解析不到 `libSystem.Native`（§2.1 的递归 FailFast） |
| 同上 | `setenv("DOTNET_Interpreter","1")` / `setenv("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT","1")` | 裸宿主没有 hostpolicy，AppContext 开关只能走环境变量；11 的 iOS 无 JIT，解释器是唯一引擎（10 的载荷需要这个开关才开解释器） |
| 同上 | 删除 `HOST_RUNTIME_CONTRACT`（`pinvoke_override` + `external_assembly_probe`） | 两者都是追错根因期间的产物；删掉后配置与 IOSClrDemo 一致，真机复验通过（`coreclr_initialize => 0x0` → `ScriptGameMode_C` → `LV_OpenWorld`） |
| 同上 | 属性键值 `static_assert` | 之前删 key 忘删 value，9 个值配 8 个键，后面按位置整体错位（运行期表现为"HOST_RUNTIME_CONTRACT 完全没生效"） |
| `MonoSDK.Build.cs` | 预签名缓存升级为双因子 | 与 §2.1 对齐；原来只按内容哈希，换证书不重签 |
| `CSGlueGenerator.cpp` | commandlet 下跳过 glue 写出 | cook 期间模块启动早于插件 tag 源与碰撞设置注册，会把已提交的 glue 覆盖成缺失子集 |
| `NativeCallbacksWrapperGenerator.cs` | string→`char*`（UTF-8 + NUL）、`ref/out`→`T*`、`bool`→`byte` 的 ABI 双胞胎 + `US0015` 诊断 | 解释器拒绝需要 P/Invoke 封送的 calli（`interpreter/compiler.cpp` "PInvoke marshalling for calli is not supported"）；`PInvoke::MarshalingRequired` 对 BYREF/BOOLEAN/STRING 均返回 TRUE，10/11 一致 |
| `Tools/verify-calli-abi.py` | 生成源码后扫描 calli **调用点**，出现未双胞胎的 string/bool/ref/out 即失败 | 静态门禁，负向自检有效（人为注入原始签名能被拦下） |
| `build-android-mac.sh` / `build-ios-mac.sh` | ABI 门禁 Step 1.5；打包前清 `Binaries/<Platform>` + `Saved/StagedBuilds/<Platform>`；iOS ini 幂等重写 | UBT 不清理旧暂存产物（上一组合的框架会跟着进包）；iOS 的 ini 之前是追加写，跑一次多一个 `[UnrealSharp]` 块 |

---

## 4. 关键结论的证据

### 4.1 签名是 iOS CLR 崩溃的真因

- 包内检查：`codesign -dv` 对 `Frameworks/CoreCLR.framework/Frameworks/*.dylib` 报
  `code object is not signed at all`；同一时期 Mono 包（走 MonoSDK 的预签名）全部 signed 且真机正常。
- 设备日志（pkg14）：`coreclr_initialize => 0x0` 之后崩在
  `Interop+Kernel32.GetEnvironmentVariable` → `DllNotFoundException` → `SR` → `CultureInfo` →
  `Environment.FailFast`；签好后的 pkg16/17 同样日志里 `FailFast|DllNotFound|signal 6` 计数为 0。

### 4.2 11 运行时承载 net10 托管

IOSClrDemo（`MANAGED_TFM=net10.0` + `CoreClrSDK/IOS/arm64`）：8/8 通过，未双胞胎的
`string / ref / bool` calli 如期抛 `InvalidProgramException`，双胞胎版本
（UTF-8 长度、校验和、NUL 终止、ref 回写、bool 取反）全部正确。结论：宿主/载荷/ABI 三条线各自独立成立。

---

## 5. 遗留与待确认

1. **Android CoreCLR 仍在 10**（§0 的例外）。迁移需要：release/11.0 worktree 构建 Android 载荷
   （本机有 `Android/arm64.v005-net11.bak` 可参考）→ 真机重跑 A/B/C/D → 删除 `*_ANDROID` 三行。
2. **`54eaeaa` 的 iOS sed 补丁在 11 上是 skip**：11 的 `src/native/libs/*/CMakeLists.txt` 已没有
   那个 pattern，所以现在走的永远是"nothing to patch"。留着无害，但若将来 11 需要新的 libs 补丁，
   要另找位置，别以为这段在生效。
3. **CoreLib 双份**：框架根一份（运行时绑定用）+ PAK 里的 `Content/Managed/IOS` 一份（TPA 列表里也有）。
   功能无碍，但白占约 1.7MB（压缩前 5.7MB）。若要瘦身，需要确认 TPA 去掉 CoreLib 不影响 binder。
4. **`M_Eye_Occlusion` 材质**：`SF_METAL_ES3_1_IOS` 编译失败回退默认材质，属内容/cook 侧，与本 SDK 无关。
5. **`7cbc428` 那次 merge 是否还丢了别的**：本次只审计了 `CoreClrSDK.Build.cs`。同类风险建议对
   `MonoSDK.Build.cs` 与 `Make*Framework.sh` 做一次 `git diff <merge-parent> HEAD` 抽查。
