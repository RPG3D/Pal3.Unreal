# 四元数/朝向跨引擎转换：问题、通用解法与当前状态

> 背景：移植 PAL3 时相机朝向错了 180°、roll 跑到 -164°，排查结论是 **Unity 语义与 UE 语义在"手性"上不一致**。
> 本文记录根因、**通用解法**（以后遇到旋转问题直接套），以及当前代码里的临时处理。

---

## 1. 三类独立的"手性/约定"问题（不要混为一谈）

| 层 | Unity | UE | 表现 |
|---|---|---|---|
| **坐标系** | 左手（X右 / Y上 / Z前） | 左手（X前 / Y右 / Z上） | 轴的**置换**，`det(P)=+1`，只换名不翻符号 |
| **四元数代数**（`q * v` 怎么转向量） | 左手习惯：`v' = v + w·(q_v×v) + q_v×(q_v×v)`（等价于绕轴**顺时针**） | UE 的 `FQuat` 与自身坐标系一致 | 同一组 (x,y,z,w) 在两边转出**相反**方向 |
| **欧拉角语义** | x=pitch(低头为正)、y=yaw、z=roll；次序 Z→X→Y | FRotator(pitch 抬头为正, yaw, roll)；次序 roll→pitch→yaw | 直接映射 `x→pitch` 会**符号相反** |

本次踩的坑：坐标系置换是对的，但**四元数代数**两边相反 → `q*(0,0,1)` 得到的是"背对目标"的方向（实测 `towardCenter` 从 -0.80 变 +0.80），另外 roll 被算成 -164°。

---

## 2. 通用解法（推荐，长期方案）

### 规则：**永远不要按分量转换四元数，转换"坐标系"本身**

```
给定源端旋转 q 与需要转换的三个基向量 e1,e2,e3（源引擎坐标轴）：
  f1 = q * e1 ;  f2 = q * e2 ;  f3 = q * e3      // 用源端的算法旋转向量
  F1 = Map(f1) ; F2 = Map(f2) ; F3 = Map(f3)     // 只做轴置换（不做符号翻转）
  目标旋转 = FromRotationMatrix(F1, F2, F3)       // 目标端用 3x3 矩阵构造，roll/pitch/yaw 全保留
```

- `Map` 只做轴重命名 + 单位缩放（方向不缩放）；
- **不要**用 `q' = (q.z, q.x, q.y, w)` 这种"向量部重标记"直接转换——它只在两端 `q*v` 语义相同时成立；
- UE 端用 `FMatrix(FPlane(F1), FPlane(F2), FPlane(F3), ...)`（或 `FQuat(FMatrix)`）而不是 `asin/atan2` 反解，能保留 roll；
- 从 UE 回 Unity 时同理：取 UE 的四元数旋转三个基向量 → 映射回 Unity 轴 → 构造 Unity 矩阵 → 转四元数。

### 自测（建议加进单测，纯托管可跑，不需要引擎）

1. `q = LookRotation(+Z)` → 转换后必须是 identity；
2. `q = LookRotation(+X)` → UE yaw 必须是 90°（不是 -90°）；
3. `q = LookRotation(+Y)` → UE pitch 必须是 +90°（不是 -90°）；
4. 任意 q 往返一次（Unity→UE→Unity）误差 < 1e-4。

这三条能覆盖 90% 的手性错误，且跑起来只要几毫秒。

---

## 3. 当前代码的处理（临时，带注释）

`Engine/Core/Implementation/UETransform.cs`：

- 所有旋转写入（`Rotation` / `LocalRotation` / `SetPositionAndRotation` / `SetLocalPositionAndRotation`）统一走 `ApplyWorldRotation` / `ApplyLocalRotation`；
- 内部用 `TryGetPitchYaw`：`fwdU = -(q * (0,0,1))` → `UnityUeConvert.ToUeDirection` → 由方向反解 `FRotator(pitch, yaw, 0)`；
- **`-` 号就是本次发现的"四元数代数手性相反"补偿**，已在注释里标明来源与移除条件；
- roll 暂时丢弃（游戏目前只有相机/角色用 look 方向，roll 无实际影响）。

### 移除条件（何时可以去掉那个负号）

把 `Script/UnityEngine/Quaternion.cs` 的代数改成 Unity 的左手约定：
`q * v` 里把 `Cross(qv, v)` 换成 `Cross(v, qv)`（以及 `LookRotation` 的矩阵→四元数符号），
然后跑上面的 4 条自测；四条全绿后，把 `UETransform` 的负号删掉，改用 `FQuat`/`FMatrix` 直接构造。

---

## 4. 已知遗留

- 相机**位置**是否也有单位/比例问题待验证（当前相机在 22m 外，房间 ±7m；可能需要确认 PAL3 相机坐标是否也按 GameBox→米换算）。
- 角色朝向：负号是全局生效的，若后续发现角色"倒着走"，说明角色路径的 q 构造方式与相机不同，需要按第 2 节改成基向量方案而不是加第二个开关。
