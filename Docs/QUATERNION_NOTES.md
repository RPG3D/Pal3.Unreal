# PAL3 坐标、四元数与朝向

本文以现行的 [UnityUeConvert](../Script/Engine/Core/Implementation/UnityUeConvert.cs)、[UETransform](../Script/Engine/Core/Implementation/UETransform.cs) 和 [Quaternion 兼容层](../Script/UnityEngine/Quaternion.cs) 为准。早期相机反向、roll 异常的排查曾使用只取前向并反号的方案，现行代码已经换成完整四元数转换。

## 坐标契约

| 数据 | Unity 侧 | UE 侧 |
|---|---|---|
| 基轴 | X 右、Y 上、Z 前 | X 前、Y 右、Z 上 |
| 位置 | 米 | 厘米 |
| 方向 | 单位不变 | `(u.z, u.x, u.y)` |

位置经过同样的轴置换，另乘 100。朝向向量只做轴置换。涉及相机、角色和网格时都从同一套坐标契约判断。

## 旋转入口

兼容层存储与 UE 四元数相同顺序的分量，并为 `Quaternion.Euler`、`LookRotation`、`AngleAxis` 和乘法提供 Unity 侧语义。经 `UETransform.Rotation` 写入时，`ToUeQuaternion` 使用 `(-x, -y, z, w)`。读回用同一变换，因此完整旋转可以往返并保留 roll。直接赋值 `UETransform.EulerAngles` 走独立的 `ToUeRotator` 路径，调整转换时要分别验证两种入口。

`UETransform.Forward` 将 Unity 方向映射到 UE，并由 `FRotator` 让 UE 的 +X 指向它。玩家输入和 NPC 路径共用上游 `ActorMovementController`，每帧经 `Forward` 写回原生朝向。此前 setter 为空，表现为角色位置变化但面朝原方向。兼容层 `Vector3.RotateTowards` 现在按角度上限旋转，水平掉头时可选出稳定的旋转轴。

## 验证

- 用 +Z、+X 和 +Y 三个方向核对 UE 的 +X 前向、+Y 右向和 +Z 上向。任意旋转往返时同时核对三个基向量。
- 用 RC 的 `pose <actorId>` 读取 `forward`，与短窗口水平位移做点积。景天左右移动和开场雪见脚本路径已经用这条链路验证。
- [开场回归脚本](../tools/pal3-check-opening-animation.ps1)同时检查原生顶点指纹和雪见转向。相机取景、脚底接地及视觉表现仍需画面对照。
