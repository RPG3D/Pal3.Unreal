# 托管 DLL 哈希命名与产物复用研究（2026-11-22）

## 结论与建议顺序

当前哈希名称有实际作用：两套同名 `UnityEngine` 私有依赖能在一个包中共存，第三方库可按完整内容复用，清单精确指向文件。不要直接改成普通的扁平 `UnityEngine.dll`，也不要更改程序集内部身份来迁就物理文件名。

优先级建议：

1. 统一可复现构建路径，复用与游戏无关的 wrapper 编译，减少相同代码因 PDB 路径不同而产生两套字节。
2. 开发日志展示“逻辑程序集名 + 短 hash”，完整 SHA-256 留在 manifest；不必先动磁盘格式。
3. 完成 Stage handler 的文件所有权迁移后，再决定包内是否使用“短内容目录 + 普通 DLL/PDB 名”。发布缓存与包内路径可以不同。

本篇仅做研究和 Saved 内隔离实验，没有修改生产命名、PathMap、wrapper 工程、模块隔离或包内容规则。上游依据为 [PR #795](https://github.com/UnrealSharp/UnrealSharp/pull/795)，固定研究 head `f3c0c3680cbe6b4b4850c809cc06a4428362a9fa`；该 PR 本身不处理哈希命名或多游戏私有依赖。

## 必须分清的四个量

| 数据 | 当前用途 | 不能替代什么 |
|---|---|---|
| Name / Identity | 例如 UnityEngine、完整版本/公钥身份，参与 C# 绑定及 native 注册 | 不能表示文件字节相同 |
| Path | 例如 UnityEngine.完整SHA256.dll，定位 UE UFS/PAK 文件 | 不是程序集内部名称，也不决定加载上下文 |
| sha256 / symbolsSha256 | 分别核验 DLL/PDB 的完整内容 | 不证明行为兼容，也不代替模块清单的身份/依赖检查 |
| shared.apiHash | 验证实际已加载共享库的公开元数据契约 | 绝不能作为 DLL 内容去重键；同 API 可以有不同方法体 |

ManagedAssemblySet、native RegisterAssemblyPaths 和 PluginLoader 已通过显式映射分离逻辑名与物理路径。因此更换物理布局通常无需改上游 gameplay 或 C++ 游戏逻辑。当前 native 名称表和一进程一套模块的约束仍在；换文件名不等于实现游戏热切换或多套同名程序集同时运行。

## 本次实测

### 包内容规模

以最终 Android 平台发布目录为样本：两份 manifest 各引用 7 个 DLL，合并后为 **12 个不同 DLL + 10 个 PDB，共 22 个文件、4,014,940 字节**。INIFileParser 与 Newtonsoft.Json 的相同字节已经复用，按两套各自复制计算节约 735,504 字节。PDB 合计 830,028 字节。

22 个文件名合计 1,766 字符。只去掉名字里的长 hash，带来的包索引/清单节约是 KB 量级，不会解决 MB 级内存增长，也没有证据说明现在的 22 个扁平文件造成目录查找瓶颈。上述是未压缩文件字节，不能当作 APK 实际压缩节约。

### 相同输入的重复发布

在 `Saved/Review/Pal3A-20261122/NamingProbe/Android` 连续运行两次完整模块发布，使用同一份 canonical Android 宿主。前后对宿主平台根的所有文件逐项 SHA-256，确认输入未变。两份游戏的全部 14 项 DLL 引用及其已有 PDB 的 hash 都稳定。

这是同机增量发布实验，不等于已经证明跨机器、干净构建或每次重新编译宿主也完全一致。证据：`NamingProbe/Comparison.json`、`Publish-1.log`、`Publish-2.log`。实验只替换 Saved 下的独立发布输出，没有替换 Content/Managed/Android 或安装包；工具会更新生成的发布 stamp。

### UnityEngine 重复产物的原因与隔离验证

两游戏当前各带一份 UnityEngine，DLL 都为 50,688 字节，PDB 约 30 KB。元数据探针发现：

- 完整程序集身份相同，公开 API 指纹相同。
- 所有方法体的 IL 字节序列摘要相同，MVID 与 DLL/PDB 内容 hash 不同。
- PDB 的三份生成文件路径包含 `.../pal3/obj/UnityEngine/...` 和 `.../pal3a/obj/UnityEngine/...`。

随后在 Saved 下各建两套**全新** wrapper 输出，保留原工程和两个 PalVariant 参数：

| 实验 | DLL 是否相同 | PDB 是否相同 |
|---|---|---|
| 对照：两个不同 artifacts 路径，无 PathMap | 否 | 否 |
| 仅把各自 obj/UnityEngine 映射到同一规范路径 | 是 | 是 |

这支持“生成文件路径影响可复现产物”的判断，且提供了无需改 wrapper 源码的处理方向。证据：`NamingProbe/UnityEngine-metadata.json`、`NamingProbe/PathMap/Comparison.json`。PathMap 实验没有装进游戏，也未验证远程/移动调试的源码定位。

不能只因为 API/IL 摘要相同就把任意两份库强行合并；真正复用应由一致构建输入或最终完整字节相等来决定。本例进一步获得了规范路径后的完整 DLL/PDB 字节一致证据。

## 布局方案比较

| 方案 | 优点 | 代价/限制 | 判断 |
|---|---|---|---|
| 保持 Name.SHA256.dll | 已验证、冲突判定简单、内容可复用 | 长文件名，DLL 与 PDB 使用各自 hash，人工查看不便 | 当前可继续使用 |
| Name.短hash.dll，清单仍存完整 SHA-256 | 小幅改动、路径短、保留扁平目录 | 必须检测短前缀碰撞，并有延长或报错策略；PDB 配对仍需处理 | 最低成本的可选显示/命名改良 |
| Objects/短内容ID/Name.dll、Name.pdb | 文件名常规、DLL/PDB 相邻，隔离同名库，不引入游戏名目录 | 要统一相对路径校验与 Stage；内容ID须覆盖 DLL/PDB 配对 | 中期优选，取决于调试便利的收益 |
| moduleId/Name.dll | 易看出游戏归属 | 重复第三方文件；重加此前取消的游戏目录层；独立程序集已表达归属 | 不作为默认建议 |
| 全部普通名、共用一个扁平目录 | 最短、接近上游单游戏发布 | 两份 UnityEngine 会覆盖，未来其他同名私有库也一样 | 不可采用 |

把完整的 64 个十六进制 hash 字符 从文件名搬到目录，本身几乎不缩短总路径；若要缩短，仍需短 ID 与碰撞处理。建议至少 20 位十六进制前缀作为可读地址，完整 256-bit hash 始终保留校验，检测到不相同内容的同前缀时延长或失败，不能覆盖。20 位只是候选策略，未经规模/碰撞测试，不是必须立即使用的协议。

若把 PDB 改成与 DLL 相同 stem，不能假定任意工具链的“相同 DLL 必然配相同 PDB”。推荐内容目录 ID 由有版本的规范记录生成，包含 DLL 全 hash、PDB 全 hash（或明确无符号标记）、逻辑名；每个文件仍单独校验。调试器是否自动读取符号还取决于当前 LoadFromStream(dll,pdb) 和调试协议，改名不会自动补齐所有调试能力。

## 与 PR #795 思路结合

推荐把三层分开：

```text
构建缓存：完整目标键 + 内容地址，保留可复用 generation
      ↓ 由 manifest 选出明确文件集合
UE Stage：映射到包内稳定的相对路径，UFS/NonUFS 各自登记
      ↓
运行时：manifest 的 Name/Identity → Path → 私有 ALC / 共享宿主
```

Stage handler 可以把构建缓存长路径映射成更清楚的包内路径；Windows/macOS 启动所需普通共享 DLL 名继续按运行时要求保留。移动端私有 DLL 仍通过 UE API 从 PAK 读取。不要采用复制完包再重命名、再修清单的后处理方式。

若普通文件名只存在于**构建缓存目录**而包内继续使用哈希名，也能改善调试/检查体验，迁移成本更低。没有必要为日志中的一串长名字立即更改发布协议。

## 分步优化方案

### A：先改善构建输入和复用

1. 规范 repo 源路径与每工程生成文件路径，配置 Deterministic/PathMap；将 target/SDK/reference 身份纳入缓存键。规范生成路径不能把 PAL3/PAL3A 的条件符号或程序集名抹平。
2. UnityEngine wrapper 与游戏变体无关的构建可以按平台、架构、配置、目标类型、TFM、backend、共享 glue/SDK 输入编译一次，两份清单复用同一物理产物。仍作为每套模块的私有程序集，不能因此提升为共享加载身份。
3. 正式实施前验证 SourceLink/本地源码定位、Mono/CoreCLR 符号、PDB 校验和两游戏行为。跨 checkout 的可复现性另做两目录干净构建对照。
4. 对整套宿主发布也做相同输入的干净对照；当前短生命周期发布目录是否影响所有项目尚未实测，不能凭 wrapper 的结果宣称整条流水线已经可复现。

这个步骤预计可省一份 wrapper 的编译工作及约 80 KB 未压缩产物，具体耗时和 APK 节约仍需正式量测。价值主要是输入稳定和缓存可复用，不夸大眼前包体收益。

### B：整理可读性与符号策略

日志/诊断默认输出逻辑名、版本、短 hash、模块 ID；完整物理路径按需要展开。Development 保留符号，Shipping 是否随包应由明确策略控制；完整 PDB 可单独归档，不靠随手删文件名来减包。

### C：在 Stage 集成后选择物理布局

如确定需要普通文件名，统一改发布器、Stage 及 manifest 验证。运行时 ResolveFile 已允许安全相对子目录，native 的逻辑名/路径 API 也不要求平铺；当前发布器 ValidateBundle 与 SharpDemo.Build.cs 的“flat filename”检查则必须同步迁移。若由 handler 接管 staging，后者应退出 app 文件登记，避免长期保留两套规则。

即使 schema 2 的 Path 技术上能表达子目录，当前旧 Stage 代码不接受它，也不能把格式升级静默发给旧工具。应显式提高布局版本/能力要求，或一次性重建所有 producer/consumer；保留拒绝旧宿主、完整文件校验、实际共享映像证据、BCL 所有权和一进程一套模块约束。

验收至少包括：两套不同 UnityEngine 同时随包但只加载所选版本、相同第三方产物去重、损坏 DLL/PDB、同短 ID 不同全 hash、重复/遍历路径、失败后旧 bundle 保留、跨平台 PAK 读取、移动符号加载、无历史产物积累。命名优化不要与游戏热切换、卸载或 gameplay 改造合成一次大修改。

## 本轮没有实施的内容

生产 PathMap/共享 wrapper 缓存、短 hash、内容目录布局、Shipping 符号政策、Stage handler 均只提出方案。现有两个游戏、自有菜单、大地图和 99 个手动存档保持原有功能；没有为命名研究修改上游 C# 或 native 游戏代码。
