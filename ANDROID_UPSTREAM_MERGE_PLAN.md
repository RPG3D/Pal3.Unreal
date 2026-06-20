# AndroidClr 分支更新上游 main 方案

> 日期：2026-06-20。研究如何把 AndroidClr 分支更新到上游 main 最新，再提交我们的改动。

## 当前状态

- **AndroidClr 分支**：HEAD = `872e444b`（与 main 相同），我们的改动是**工作区未提交**（未 commit）
- **origin/main 最新**：`a2d2cd47`（AndroidClr 落后 9 个提交，5.8 fixes 等）
- **remote**：origin / rpg3d 都是 `RPG3D/UnrealSharp.git`

## 上游 9 个提交内容
- 5.8 兼容修复（crash、plugin 适配）
- `Bind_AsyncExporter` → `Bind_Async` 重命名
- `Bind_FMulticastDelegateProperty` / `Bind_UFunction` / `Bind_IRefCountedObject` 改动
- `CSManager.cpp` include 调整（删 `Engine/UserDefinedStruct.h`，加 `Misc/CoreDelegates.h` + `UObject/Package.h`）
- `UnrealSharp.Automation.props` 简化（删 `ImportUE5RulesDefineConstants` target）
- README 版本号

## 冲突分析

**重叠文件仅 1 个**：`Source/UnrealSharpCore/Private/CSManager.cpp`
- 上游改：文件头部 include（L8-12）
- 我们改：L191+（collectible `#if`）+ L226+（RF_MarkAsRootSet）
- **不冲突**——不同区域，git merge 可自动合并 ✅

**其余文件无重叠**：上游改的 19 个文件中，我们只碰了 CSManager.cpp。我们的其他改动（host、CoreClrSDK、PluginLoadContext 等）上游没动。

**潜在间接影响**：
- `UnrealSharp.Automation.props` 简化——影响 BuildManagedCode 的 publish 流程（DefineConstants 注入方式变了）。我们用 `-p:DisableWithEditor=true` 直接传参，不依赖该 target，**不受影响**。
- `Bind_Async` 重命名——编译时改动，我们没碰这些文件，**不受影响**。

## 推荐方案：先 stash → merge → unstash

最安全，不丢工作区改动：

```bash
cd Plugins/UnrealSharp

# 1. 暂存我们的工作区改动（含未跟踪新文件）
git stash push -u -m "Android CoreCLR port (pre-merge)"

# 2. 确认工作区干净
git status

# 3. merge 上游 main（9 个提交）
git merge origin/main
# 预期：自动合并成功（CSManager.cpp 不同区域，无冲突）

# 4. 恢复我们的改动
git stash pop
# 预期：自动 apply 成功。若 CSManager.cpp 有小冲突，手动解（我们的 #if 和 RF_MarkAsRootSet
#       在 L191+/L226+，上游改 L8-12，应该无冲突）

# 5. 验证编译
# Win64 Editor
# Android
# 若上游 5.8 改动影响编译，单独修

# 6. 提交我们的改动（用户操作）
# git add ...
# git commit -m "feat: Android CoreCLR host ..."
```

### 备选方案：rebase（不推荐）
`git rebase origin/main` 把我们的"改动"rebase 到 main 之上——但我们的改动是**未提交的工作区**，rebase 不直接处理工作区。需先 commit 再 rebase，多一步且改写历史。stash 方案更简单。

### 备选方案：reset 到 main + 重新 apply patch
```bash
git checkout main
git pull
git checkout -b AndroidClr_new
git apply E:/Code/UnrealSharp-AndroidCoreClr-20260620-123945.patch
```
最干净（新分支基于最新 main），但放弃当前 AndroidClr 分支历史。若 AndroidClr 还没 push 过，这个最简单。

## 风险

1. **CSManager.cpp merge 冲突**：低（不同区域）。若冲突，手动保留两边的改动（上游的 include + 我们的 `#if`/root set）。
2. **上游 5.8 改动编译问题**：中。上游 5.8 fixes 可能引入我们没适配的 API 变化。merge 后需 build Win64 + Android 验证。
3. **Automation.props 简化**：低。我们不依赖被删的 target。

## 推荐执行顺序

1. **先备份 patch**（已有 `E:/Code/UnrealSharp-AndroidCoreClr-20260620-123945.patch`）✅
2. **stash 我们的改动**
3. **merge origin/main**
4. **stash pop**（恢复改动）
5. **build 验证**（Win64 Editor + Android）
6. **若编译失败**：修冲突/适配 5.8 改动
7. **提交**

## 注意

- AndroidClr 分支是否已 push 到 origin？若已 push，merge 后 push 需要 `--force`（如果用 rebase）或直接 push（merge commit）。stash+merge 方案产生 merge commit，直接 push 即可。
- 建议在干净状态（stash 后）先验证 main 能编译，再 pop 我们改动。
