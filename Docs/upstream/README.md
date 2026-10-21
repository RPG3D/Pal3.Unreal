# Upstream 沟通与 `preparemobile` 分支

本文档集合的目标：把 MobileClr 分支里**与移动端无关、对上游同样有价值的修复**单独拆出来，
整理成可以直接发给 UnrealSharp 作者的 PR 素材，降低长期维护的合并成本。

## 1. 当前分支拓扑

| 分支 | 基线 | 内容 | 用途 |
| --- | --- | --- | --- |
| `main`（插件本地） | 上游 `b0bd7c62` | 跟随 origin | — |
| `mobileclr202609` | `b0bd7c62` + 12 个提交 | 完整移动端 CoreCLR 线（Android/iOS 已验证） | 产品分支 |
| `mono20261010` | 上游 + Mono 相关提交 | Mono 线 | 另一条产品分支 |
| `preparemobile` | 上游最新 `main`（`971c81c3`） | **只放通用 fix**，从零开始 | 给上游发 PR |

`preparemobile` 创建于 2026-10-21，指向 `upstream/main`，比 `b0bd7c62` 新 93 个提交。
它目前是空的（等于上游），通用 fix 以补丁形式存放在 `patches/`，等确认后再逐个提交上去。

## 2. 上游现状（决定了 PR 的写法）

- 上游**没有**移动端 CoreCLR 支持：没有 `Source/ThirdParty/CoreClrSDK`，没有
  `CSDotNetRuntimeHost_Mobile.cpp`，也没有 `coreclr_initialize` 相关代码。
  因此移动宿主本身是一个新特性，不适合混在「修 bug」的 PR 里。
- 上游已经吸收了 `901cfce5 Fix hostfxr character pointer conversions on macOS`：
  我们 `3f5074c1` 里的 `char8_t` / `StringCast` 部分**已经过时**，发 PR 时不要再带。
- `NativeCallbacksWrapperGenerator.cs`、`CSGlueGenerator.cpp` 自 `b0bd7c62` 以来上游没改过，
  补丁可以干净地应用。
- 上游仍是 `net10.0`，我们这条线是 `net11.0`（10 个 csproj/global.json 的一行改动）。
  **不要**把 TFM 升级塞进任何一个通用 fix 的 PR。

## 3. 建议的 PR 顺序

由小到大、由无争议到需要讨论：

1. `PR-04` RuntimeGlue commandlet 守卫（约 10 行，纯 bug fix，最容易接受）
2. `PR-02` hostfxr Windows 路径分隔符（约 10 行，纯 bug fix）
3. `PR-03` iOS 私有 PCH（1 个新头文件 + 3 处 Build.cs，平台限定）
4. `PR-01` calli ABI twin（生成器改动，最大，但收益最高，建议先开 issue 讨论）

每个 PR 都应该是**独立可编译、独立可回退**的。不要把四个合成一个提交，
也不要在一个 PR 里引用 `CoreClrSDK` 子模块。

## 4. 在 `preparemobile` 上落地补丁

```bash
cd Plugins/UnrealSharp

# 确保 mobileclr202609 的工作区已提交（当前已提交到 2c2a1938）
git switch preparemobile

# 按 PR 拆分应用（补丁已针对 upstream/main@971c81c3 重新生成并校验）
git apply /path/to/SharpDemo/Docs/upstream/patches/0001-calli-abi-twin.patch
git apply /path/to/SharpDemo/Docs/upstream/patches/0002-runtime-glue-commandlet-guard.patch

# 每个 PR 单独提交、单独推送，并向上游开 PR
git switch -c pr/calli-abi-twin
git commit -am "fix(coreclr): ..."
git push origin pr/calli-abi-twin
```

补丁是针对 `preparemobile` 的当前 HEAD 生成的，`git apply --check` 已验证可直接应用。
如果之后上游又有新提交改到这两个文件，先 `git rebase upstream/main`，再按上游风格调整。

## 5. 和作者沟通时的要点

- 先说明背景：我们在做 **CoreCLR 移动端（iOS 无 JIT，必须走解释器）**，
  这几项修复是在该场景下暴露出来的；其中 ABI twin 对 JIT 平台同样是修正（bool 的 4 字节问题）。
- 明确边界：移动宿主、CoreClrSDK 构建脚本、打包命令属于我们自己的分支，不要求上游接收；
  只请求接收与平台无关的修复。
- 主动提供验证方式：Android 真机 + iPadOS 真机 + macOS Editor 的编译/运行结果。
- 对 `PR-01` 给出替代方案的评估：把 `string` 改成 `char*` 需要改所有调用点，
  在生成器里做 ABI twin 是调用点零改动的方案；如果上游更倾向别的形态，可以配合调整。

## 6. 文档索引

| 文件 | 内容 | 语言 |
| --- | --- | --- |
| `PR-01-MECHANISM.md` | **为什么必须改生成器**：JIT vs 解释器、.NET 10 vs 11 的源码级机制与实验证据 | English |
| `PR-01-calli-abi-twin.md` | 生成器 ABI twin（string/ref-out/bool）PR 草稿 | English |
| `PR-02-hostfxr-path-separators.md` | hostfxr Windows 路径分隔符 PR 草稿 | English |
| `PR-03-ios-private-pch.md` | iOS 私有 PCH PR 草稿 | English |
| `PR-04-runtime-glue-commandlet-guard.md` | RuntimeGlue 在 commandlet 中不写文件 PR 草稿 | English |
| `MOBILECLR-RATIONALE.md` | 这 12 个提交各自为什么存在、哪些可上游化 | 中文 |
| `patches/*.patch` | 当前工作区导出的通用 fix（未提交） | — |

## 7. 待办

- [ ] 在真机上复测 ABI twin 的 NUL 终止 + stackalloc 版本（Android + iPad）
- [ ] 决定 `PR-01` 是先开 issue 还是直接发 PR
- [ ] 若上游接受 `PR-04`，把本地 `mobileclr202609` 对应提交 rebase 掉
- [ ] 上游迁到 .NET 11 后，删除本分支的 TFM 升级提交

## 8. 机制分析的复现方式

`PR-01-MECHANISM.md` 的结论来自两份 dotnet/runtime 源码对照和一个本机实验：

- `.NET 11` 源码：`/Users/admin/Documents/Code/DotNet`（`release/11.0`）
- `.NET 10` 源码：`/Users/admin/Documents/Code/DotNet10`
  （`git clone --filter=blob:none --sparse --depth 1 --branch release/10.0`，
  sparse checkout `src/coreclr/{vm,interpreter,inc,jit}`）
- 实验：任意 `net10.0`/`net11.0` 控制台程序，`AllowUnsafeBlocks=true`，调用
  `libSystem` 的 `strlen`：

  ```csharp
  IntPtr lib = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
  IntPtr strlen = NativeLibrary.GetExport(lib, "strlen");
  var f = (delegate* unmanaged[Cdecl]<string, nuint>)strlen;
  Console.WriteLine(f("hello"));   // 5 => JIT 自动 marshal
  ```

  两个版本都输出 5，证明 JIT 路径由运行时插入 marshalling stub；解释器路径则没有。
