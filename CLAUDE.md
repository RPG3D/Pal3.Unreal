# SharpDemo 项目规则

## 1. 编辑器启动超时（最高优先级）
- 启动编辑器（含 `-game` 模式）后，**最多等待 5 分钟**，超时就强制关闭进程（`Get-Process UnrealEditor | Stop-Process -Force`）。
- 否则编辑器进程会卡住会话 context，导致无法继续迭代。
- 等待期间用 `sleep` + 轮询日志，一旦拿到所需日志结论（Pal3Boot / SMOKE / threw 标记）就提前收尾，不必等满 5 分钟。

## 2. 标准迭代循环（改 stub → 验证）
每次修改 managed 代码后按此循环：
1. `cd /d/UEProject/SharpDemo/Script && dotnet build Pal3.Game/Pal3.Game.csproj -c Release`（改 UnityEngine/Engine 时全 sln build）
2. 拷贝 DLL：`Script/*/bin/Release/net11.0/*.dll(.pdb)` → `Binaries/Managed/net11.0/`
3. kill + 重启编辑器：
   ```bash
   powershell -Command "Get-Process UnrealEditor -ErrorAction SilentlyContinue | Stop-Process -Force"
   powershell -Command '$env:PATH = "D:\Code\UE5\Engine\Binaries\Win64;C:\Program Files\dotnet\;" + $env:PATH; Start-Process -FilePath "D:\Code\UE5\Engine\Binaries\Win64\UnrealEditor.exe" -ArgumentList "D:\UEProject\SharpDemo\SharpDemo.uproject","LV_OpenWorld","-game","-log"; Write-Host launched'
   ```
4. `grep -n "Pal3Boot\|threw\|SMOKE\|Game resources initialized" Saved/Logs/SharpDemo.log | grep -v Mono`
- 注意：**PATH 里 `dotnet` 项必须带尾斜杠**（`C:\Program Files\dotnet\;`），否则 UnrealSharp 找不到 .NET SDK 弹模态框卡死。

## 3. 移植背景
- 把开源 Unity C# 版《仙剑奇侠传三》（`D:\Code\Pal3.Unity`）通过 UnrealSharp 移植到 UE5。
- 游戏数据在 `E:\Program Files (x86)\Steam\steamapps\common\PAL3\`（可用 `PAL3_DATA_PATH` 环境变量覆盖）。
- 运行模式：UnrealSharp 子模块在 **MobileClr202609 分支 = CoreCLR（.NET 11）**。**仅 Android/iOS 走 raw coreclr（PAK 解压方案）；桌面平台保持插件上游默认机制（hostfxr），不做本地定制**——减少后续合并上游的冲突。DefaultEngine.ini 里的 `bUseMono=true` 是历史残留，**无效**。
- `-game` 不重编译 managed 代码，靠手动拷贝 DLL 到 `Binaries/Managed/net11.0/`。

## 4. 打包测试（聚焦 Mobile）
- Android 打包：`cmd //c "D:\UEProject\SharpDemo\BuildAndroid.bat" Development`（后台 + 轮询 log；步骤标记 `[ OK ] Step N` / `[FAIL]`；硬超时 40 分钟，日志停滞 3 分钟视为死锁）。
- 桌面平台**不折腾**：跟随插件上游（fb3ba662）行为。曾实验过 Win64 PAK 加载/直载并验证通过，因维护成本回退，完整记录见 KB `D:\AiAgentKB\ue-win64-pak-bcl-raw-coreclr\README.md`（含三个平台坑与 probe 直载实现，未来需要可复用）。
- 成功标记（Android 设备 logcat / 运行日志，按顺序）：
  - `[CoreClr] Extracted N, skipped M ... Content/Managed/Android`（二次启动应大量 skipped）
  - `[CoreClr] coreclr_initialize => 0x0` → `coreclr_create_delegate => 0x0`
  - `Discovered N load order manifests` + `Successfully loaded plugin`
  - Pal3Boot 标记 + `[Pal3Boot] SMOKE PASS`
- 机制（mobile）：共享框架 managed+native dll 全部 UFS 进 PAK（`Content/Managed/Android/`），boot 解压到 `Saved/Managed/Android/`，coreclr 从解压目录取用，TPA 按 PE CLR header 只收 managed。
