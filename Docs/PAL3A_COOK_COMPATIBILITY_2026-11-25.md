# Cook 上游工作流兼容性修复（2026-11-25）

复审发现插件把所有 Cook 当成 UAT 子实例，并强制禁止 automation 模块编译。独立 Cook 在项目脚本构建记录为空时找不到 BuildUserSolution；预先运行项目包装工具可以掩盖该问题。

修复位于 UnrealSharp 的 CSBuildUtilties.cpp，没有添加项目名、游戏条件或新配置协议：

- 仅在 Cook 继承了 UAT 的 uebp_EngineSavedFolder、uebp_LogFolder 上下文时，设置 UAT 子实例和互斥协议；调用结束后恢复原环境值。
- 托管 Cook 只使用 NoCompileUAT 跳过正在运行的 UAT 主程序自编译，automation 模块沿用正常缓存、发现和编译，不再强制 NoCompile。
- 独立 Cook 沿用普通 UAT 启动路径；CrashForUAT 标志本身不代表存在外层 UAT。
- 始终调用 BuildUserSolution，保留真实用户 C# 构建及失败传播。

验证使用本机 UE5.9 / Mac / Mono 编辑器及真实项目，没有启动模拟器或改引擎源码。

| 场景 | 结果 |
|---|---|
| 普通独立 Cook，项目 automation 构建记录为空 | 完整 Cook 退出 0，重建构建记录和两份探针 DLL |
| 独立 Cook，仅带 CrashForUAT，构建记录为空 | 完整 Cook 退出 0，仍使用普通 UAT 路径，重建两份探针 DLL |
| 外层 BuildCookRun 托管 Cook，构建记录为空 | 外层退出 0，使用子实例协议，重建两份探针 DLL |
| 托管 Cook，故意在共享用户源码加入 C# 编译错误 | Editor-Cmd=1、外层 UAT=25，没有 Cook 成功记录 |

探针使用同目录的两个普通工程；每次替换不同的返回字符串，并核对两个输出 DLL 中的实际字符串，证明 C# 没有被跳过。进程参数采样确认独立入口没有 NoCompileUAT/NoCompile，托管入口只有 NoCompileUAT。每次成功输出仍为 16 个普通启动根、Collectible=true。

最终恢复源码并重新准备 Editor 产物。24 项托管检查、5 项清理测试通过；Cook 编辑器峰值采样 RSS 2600 MiB，12 GiB 保护未触发，全部编辑器进程退出。

本地 Saved 中的历史源码备份以 UE 标准 .ubtignore 排除规则扫描，该生成目录标记未提交；正式存档、Rider GUID 和个人配置保持原样。

原始记录位于 Saved/Review/Pal3A-Cook-Compatibility-Fix-20261125：三个 Cook 结果、故障退出、进程命令行、源码恢复、检查日志和内存采样。本轮修改仅影响编辑器构建路径，沿用前一轮 APK/IPA 真机验证，不据此增加 Windows、移动 CoreCLR、Shipping/R8 或长时间泄漏检测的通过范围。
