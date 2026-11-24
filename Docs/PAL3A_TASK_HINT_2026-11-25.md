# 问情篇主线提示（2026-11-25）

直接在 Demo main 接入 PAL3.Unity 已有任务提示，只用于 PAL3A；UnrealSharp 与 SDK 没有新增修改。

## 行为与分层

- 上游 TaskManager 从原版 `data/script/task.txt` 读取任务定义，TaskOpen 更新主线说明，支线任务不覆盖主线；任务完成、重置、游戏状态通知及存档恢复沿用上游实现。
- TaskManager 只增加两个只读呈现属性，UE HUD 读取已有 TMP 兼容组件中的文本与 enabled 状态，没有另建任务规则或剧情编号映射。
- Gameplay 下显示有内容的提示，其他状态沿用上游 enabled 控制；标题、无任务、重置及新游戏剧情阶段隐藏。
- 布局为 `Script/Pal3.Frontend/Layouts/Pal3TaskHint.json`，右上方避让 Menu，自动换行，纯显示且不接收点击；同一 widget 随任务更新复用，结束会话时释放。
- JSON 文字新增可选 `shadowOffset`、`shadowColor` 样式，仅提供这些字段时覆盖 UTextBlock 默认阴影。布局、字号、颜色和位置仍由 JSON 定义。
- 原版任务文本从用户数据读取，没有把 task.txt 或提示内容打入工程；游戏内容沿用原语言。

对照来源：[TaskManager](https://github.com/0x7c13/Pal3.Unity/blob/main/Assets/Scripts/Pal3.Game/GameSystems/Task/TaskManager.cs)、[Pal3.prefab](https://github.com/0x7c13/Pal3.Unity/blob/main/Assets/Resources/Prefabs/Pal3.prefab)。上游 TaskInfoBar 位于右上方；UE 版本为移动端 Menu 预留上方空间。

## 验证

编辑器使用隔离 UserDir 与存档副本：主线 071、支线不覆盖、主线 072、任务完成行为、无任务隐藏、实际存档任务 140、重置/新游戏视频隐藏，以及仙剑三不创建此 widget 均通过。实际读取 UMG 文本、可见性、阴影与坐标，确认提示不拦截输入、与移动端 Menu 不重叠；两轮画面检查通过。

Android 与 iOS 均完整重新编译、Cook、签名、封装并覆盖安装，使用 Mono / Development / arm64。核对实际包中普通程序集、布局及 SDK 文件，保留 PAL3 图标，未包含原版 CPK/BIK/MP4。

两台真机各完成主线/支线提示、下一主线、无任务、实际存档恢复、保存后重启读档、目录读取复用及仙剑三回归。已检查实际截图，提示位于 Menu 下方；iPad 两份 bookmark 保留。Android 三份、iPad 九份原存档恢复后哈希一致，临时槽移除，原游戏选择恢复。

短时采样峰值：Android PSS 875 MiB，iPad physical footprint 1045 MiB，编辑器 RSS 3883 MiB。口径不同，不作平台比较；未见持续失控增长，未覆盖长时间游玩、移动 CoreCLR、Shipping/R8 或旧版 iOS。没有启动模拟器。

iPad 首次 DVT 启动未生成游戏自动化会话，已恢复数据后切换到已验证的启动方式，完整测试通过；该工具启动失败未计为游戏通过或游戏崩溃，原始日志保留。

证据：`Saved/Review/PAL3A-TaskHint-20261125`、`Saved/Packaging/PAL3A-TaskHint-20261125`、`Saved/DeviceQA/PAL3A-TaskHint-20261125`。安装包位于 `Packaged/PAL3A-TaskHint-20261125`，哈希见 SHA256SUMS.txt。设备标识、授权数据、截图、安装包和原版资源未提交，本地 Rider GUID 与个人配置保持原样。
