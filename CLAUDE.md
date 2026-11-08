# SharpDemo 项目工作说明

项目结构、Win64 构建与启动、PAL3 操作和当前待办见 [README](README.md)。运行时诊断、RC 与回归见 [Docs/DIAGNOSTICS.md](Docs/DIAGNOSTICS.md)。

## 桌面迭代

- 托管修改发布到 `Binaries/Managed/net10.0`。原生接口变更先构建 `SharpDemoEditor` 并更新对应 glue。启动失败时先检查构建退出码、实际加载的 DLL 和运行日志。
- 默认测试地图为 `LV_Pal3`。单独启动的测试进程使用独立 `-abslog`，启动最多等待 5 分钟。结果已取得或启动超时时保留日志，只结束本次启动的测试进程。
- 自动化使用 PAL3 游戏命令。起动回归前为测试进程设置独立的 `PAL3_AUTOMATION_DIR` 和 `PAL3_SAVE_DIR`。
- UI 源布局使用 JSON，交互与状态由 C# 管理，原生模块提供通用接口。优先沿用 PAL3.Unity 的 C# 玩法，在 wrapper 修复引擎差异。

## 运行时与移动端记录

- 当前桌面开发配置见 `Config/DefaultEngine.ini` 的 `[UnrealSharp]`。编辑器使用 `DotNetRuntimeEditor=Mono`，项目 TFM 与 SDK 见 `Script` 下的项目文件和 `global.json`。运行时改动归属 `Plugins/UnrealSharp` 子模块，提交时单独检查其工作区和主项目 gitlink。
- `BuildAndroid.bat` 是 Windows Android 打包入口。移动端运行时实验、切换背景和既有验证记录见 [Docs/rebuild/SESSION-STATE.md](Docs/rebuild/SESSION-STATE.md) 及 [Docs/upstream/README.md](Docs/upstream/README.md)。当前 PAL3 回归以 Win64 为基准，移动端应独立验证。
