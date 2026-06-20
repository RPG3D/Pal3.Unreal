# Android 测试操作手册（adb 模拟触摸 / 摇杆 / 验证流程）

> 复用文档：装机、启动、logcat、模拟触摸摇杆、检查崩溃。设备 2136×3200 @440dpi。
> 关联：`BUILD_ANDROID.md`（打包）、`ANDROID_BUILD_NOTES.md`（踩坑）、`ANDROID_GC_CRASH_ANALYSIS.md`（GC）。

## 1. 设备连接 & 装机

```bash
# 确认设备
adb devices                        # 应显示 device

# 卸载旧的 + 装新 APK
adb uninstall com.YourCompany.SharpDemo
adb install -r ArchivedBuilds/Android/SharpDemo-arm64.apk

# 清 logcat
adb logcat -c
```

包名 `com.YourCompany.SharpDemo`，启动 Activity `com.epicgames.unreal.SplashActivity`。

## 2. 启动 App

```bash
# 用 monkey 启动（最简单，触发 LAUNCHER intent）
adb shell monkey -p com.YourCompany.SharpDemo -c android.intent.category.LAUNCHER 1

# 或显式启动 SplashActivity
adb shell am start -n com.YourCompany.SharpDemo/com.epicgames.unreal.SplashActivity
```

## 3. 模拟触摸 / 摇杆（核心）

屏幕 2136×3200。摇杆/触屏走 EnhancedInput 的 Touch 1（移动）/ Touch 2（镜头）。
`adb shell input` 命令模拟触屏，UE 的 Touch 输入会收到事件。

### `input swipe x1 y1 x2 y2 duration_ms` — 滑动（按→移→抬）

```bash
# 摇杆向右移动（左半屏按下，向右拖）
adb shell input swipe 500 1600 1500 1600 300

# 摇杆向左移动
adb shell input swipe 1500 1600 500 1600 300

# 镜头旋转（右半屏滑动）
adb shell input swipe 1500 1000 500 1000 300
```
- `duration` = 滑动总时长(ms)。摇杆持续移动用较长 duration（如 500-1000）。
- 起点是按下位置，终点是抬起位置。摇杆区域一般是左半屏中下。

### `input tap x y` — 单击

```bash
adb shell input tap 1000 1600     # 点击屏幕中央
```

### 持续按住摇杆（模拟持续移动）

`swipe` 是一次性（按下→抬起）。要持续按住摇杆移动，用长 duration swipe，或循环短 swipe：

```bash
# 循环滑动让角色持续移动 10 次（每次 300ms）
for i in $(seq 1 10); do
  adb shell input swipe 500 1600 1500 1600 300
  adb shell input swipe 1500 1600 500 1600 300
done
```

### `input motionevent` — 精细控制（按下/移动/抬起分离）

需要持续按住不抬起时，用 `motionevent` 序列（较复杂，一般 swipe 够用）：
```bash
# 按下 -> 移动 -> 抬起（三步）
adb shell input motionevent DOWN 500 1600
adb shell input motionevent MOVE 1500 1600
adb shell input motionevent UP 1500 1600
```
注：部分设备/Android 版本 `motionevent` 支持不一，swipe 兼容性最好。

## 4. logcat 过滤（看 CoreCLR / 托管 / GC）

```bash
# 实时跟随
adb logcat -s CORECLR:E LogUnrealSharp:I DOTNET:I LogUnrealSharpPlugins:I LogGarbage:W

# 一次性 dump
adb logcat -d | grep -iE "CoreClr-Android|UnrealSharp|DOTNET|LogGarbage"

# 只看 UE 进程
adb logcat -d | grep "UE "
```

关键日志（成功序列）：
```
[CoreClr-Android] coreclr_initialize => 0x0
UnrealSharp initialized successfully.
Discovered 1 load order manifests.
Successfully loaded plugin: 'ManagedSharpDemo' / 'SharpDemo' / ...
```

## 5. 检查崩溃 / GC 验证

```bash
# GC 崩溃（Disregard for GC 违规）
adb logcat -d | grep -iE "Disregard for GC object|GarbageCollectionVerification|breaking Disregard|Encountered.*Disregard"

# 任何 Fatal
adb logcat -d | grep -iE "Fatal error|SIGSEGV|tombstone" | grep -iE "unreal|sharp|coreclr"

# app 是否还活着（返回 PID = 活着，空 = 退了）
adb shell pidof com.YourCompany.SharpDemo
```

## 6. 强制触发 GC（验证 GC 假设）

UObject GC 在对象数达阈值时触发，full purge 时跑 `VerifyGCAssumptions`。
要主动触发：
- **操作角色**：触屏滑动让角色移动，产生 managed object 分配（`FindManagedObject` 等），撑满 GC 阈值。
- **长时间运行**：app 跑几分钟，自然触发 full purge。
- 控制台命令 `gc.CollectGarbage`（Android 上发命令较麻烦，一般靠操作触发）。

之前 GC 崩溃在第 638 帧（~10 秒）。修复后跑 250+ 秒无崩溃即视为修复有效。

## 7. 强制停止 / 重启

```bash
adb shell am force-stop com.YourCompany.SharpDemo
adb shell pm clear com.YourCompany.SharpDemo    # 清数据（强制重新提取 DLL）
adb shell monkey -p com.YourCompany.SharpDemo -c android.intent.category.LAUNCHER 1
```

## 8. 查看提取的运行时 DLL

```bash
# CoreCLR host 提取的目录（PersistentDownloadDir/Managed/Android）
adb shell ls /storage/emulated/0/Android/data/com.YourCompany.SharpDemo/files/Managed/Android/

# 看 LoadOrder manifest
adb shell cat /storage/emulated/0/Android/data/com.YourCompany.SharpDemo/files/Managed/Android/UserCode.LoadOrder.json
```

## 9. 完整测试脚本（一键）

```bash
APK="ArchivedBuilds/Android/SharpDemo-arm64.apk"
PKG="com.YourCompany.SharpDemo"
adb uninstall $PKG; adb install -r "$APK"
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1
sleep 15
# 模拟操作 + 等待 GC
for i in $(seq 1 10); do
  adb shell input swipe 500 1600 1500 1600 300
  adb shell input swipe 1500 1000 500 1000 300
done
sleep 60
# 检查
echo "=== GC 崩溃? ==="
adb logcat -d | grep -iE "Disregard for GC|GarbageCollectionVerification|breaking Disregard" | head
echo "=== app 活着? ==="
adb shell pidof $PKG
```

## 备注

- `adb shell input` 坐标基于**物理像素**（2136×3200），不是 dp。
- 摇杆精确位置取决于游戏 UI 布局，先用 `input tap` 点几下确定摇杆区域，再 swipe。
- 长按/持续移动用长 duration swipe 或循环短 swipe。
- 触屏 Touch 事件映射见 `AScriptCharacter.cs`：`Touch 1` = MoveAction，`Touch 2` = LookAction。
