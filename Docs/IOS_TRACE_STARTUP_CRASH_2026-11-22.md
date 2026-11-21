# iPad Trace 启动崩溃记录（2026-11-22）

状态：记录并暂缓修复，未修改引擎。

## 发生频率与处理决定

本轮保留了 17 个不同 PID 的启动记录，其中 1 次具有明确的 Trace 原生崩溃报告；另有 1 次升级后的启动退出，未取得对应报告，不能认定为相同问题。其余多次启动完成了游戏加载或后续测试。

这些启动包含不同参数、设备巡检服务和应用升级，既不是随机样本，也不是受控的重复实验。只能判断为本轮出现的偶发现象，不能把 1/17 当成稳定发生率或据此保证概率很低。按用户要求，先记录证据，后续再做专项调查。

## 已确认的故障

- 设备：iPad Air 11-inch M3；报告系统版本 iPhone OS 27.2.1（24B156）。
- PID：6690；启动时间 22:15:16.3745，崩溃采集时间 22:15:16.7707，约启动后 0.40 秒。
- `EXC_BAD_ACCESS / SIGSEGV`，访问地址 `0x4098`，不是 Jetsam / 内存超限退出。
- 主程序 UUID：`C86F6CB7-361F-35DF-948B-6C843988E0BE`，与本地保留的原始安装包可执行文件一致。
- GameThread 调用链：`FEngineLoop::PreInitPreStartupScreen` → `FTraceAuxiliary::Initialize` → `CallstackTrace_Initialize` → `Modules_Initialize`。
- 故障发生在 UnrealSharp 运行时初始化及 PAL3 托管游戏加载之前。Mono framework 已由系统映射，不代表 Mono VM 已完成启动。

对应引擎位置：`Engine/Source/Runtime/Core/Private/IOS/IOSModuleDiagnostics.cpp`，循环读取 `infos->infoArray[ImageInfoIndex]` 的 `imageLoadAddress`，当前代码约第 59–64 行。

### 指令与寄存器闭环

已对 UUID 匹配的二进制反汇编。故障 PC 在 `Modules_Initialize + 336`，链接地址 `0x100904fec`：

```asm
ldr  x8,  [x28, #8]       // infos->infoArray
mov  w9,  #24             // sizeof(dyld_image_info)
madd x23, x19, x9, x8     // &infoArray[index]
ldr  x21, [x23]           // imageLoadAddress；在此崩溃
```

报告中 `x8 = 0`、`x19 = 689`、`x23 = 0x4098`，因此：

```text
NULL + 689 * 24 = 16536 = 0x4098
```

这确定了直接原因：引擎把 NULL 模块数组作为有效列表，访问下标 689（第 690 项）。尚未执行后续 Mach-O header 解析，因此这份报告不支持“Mono framework 的 Mach-O header 格式损坏”这一解释。

Apple 的 [dyld_images.h](https://github.com/apple-oss-distributions/dyld/blob/main/include/mach-o/dyld_images.h) 明确说明，`infoArray` 在 dyld 修改列表时可以暂时为 NULL，应稍后再读取。本机 Xcode iPhoneOS SDK 头文件也有相同契约。引擎当前循环没有处理这个状态，而且每次循环重新读取数组和数量，没有取得一致快照。

## 并发触发线索

同一份崩溃报告中的其他线程：

- `AXBundleLoadQueue` 正在执行 `NSBundle loadAndReturnError:`、`NSBundleAccessibility loadAndReturnError:` 及 dyld 的地址/映像查询。
- `_AXDyldImageMonitor` 正在处理新加载映像。

这与“无障碍模块加载期间，GameThread 并发枚举 dyld 列表”高度吻合。本轮此前使用了 Accessibility Audit 服务读取系统文件选择器；本机工具的 `iter_elements()` 会启用 app monitoring，关闭服务连接不等同于证明系统已撤销所有巡检状态。

但没有受控对照实验，不能断言测试工具是唯一触发源，也不能断言普通启动绝不会触发。待后续调查时应分别比较：未启用无障碍检查的普通启动、启用系统无障碍功能的启动、开发巡检服务运行期间的启动。

## 后续方向，尚未实施

1. 优先避免直接遍历可被并发修改的 `dyld_all_image_infos` 裸数组，评估系统 add/remove-image 回调及一致快照方案。
2. 仅在循环前加一次 NULL 判断仍存在检查后失效的窗口；仅换成 `_dyld_image_count()` 循环也不能保证线程安全，Apple 头文件同样提示了这一点。
3. 这个初始化路径在当前非 Shipping 构建会执行；关闭 Trace 输出通道不等于跳过 `Modules_Initialize`，不能当作已验证的规避办法。Shipping 有编译期分支跳过该函数，但不代表 Shipping 已完成本轮测试。
4. 再次出现时保留完整系统报告、启动参数、无障碍巡检状态和 UUID；无需先改 PAL3 存档或游戏逻辑。

## 本地证据

- `Saved/DeviceQA/PAL3A-20261122/ipad-restart-crash.ips`
- `Saved/DeviceQA/PAL3A-20261122/trace-analysis/modules-disassembly.txt`
- `Saved/DeviceQA/PAL3A-20261122/ipad-launch*.json`

这些原始日志保留在本机，没有把设备标识、完整设备清单或游戏数据提交到仓库。
