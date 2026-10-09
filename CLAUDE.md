# SharpDemo 项目规则

## 1. 编辑器启动超时（最高优先级）
- 启动编辑器（含 `-game` 模式）后，**最多等待 5 分钟**，超时就强制关闭进程（`Get-Process UnrealEditor | Stop-Process -Force`）。
- 否则编辑器进程会卡住会话 context，导致无法继续迭代。
- 等待期间用 `sleep` + 轮询日志，一旦拿到所需日志结论（Pal3Boot / SMOKE / threw 标记）就提前收尾，不必等满 5 分钟。

## 2. 标准迭代循环（改 stub → 验证）
每次修改 managed 代码后按此循环：
1. `cd /d/UEProject/SharpDemo/Script && dotnet build Pal3.Game/Pal3.Game.csproj -c Release`（改 UnityEngine/Engine 时全 sln build）
2. 拷贝 DLL：`Script/*/bin/Release/<tfm>/*.dll(.pdb)` → `Binaries/Managed/<tfm>/`（**tfm 跟随插件分支**：Mono202610 = net10.0，MobileClr202609 = net11.0，见 §3）
3. kill + 重启编辑器：
   ```bash
   powershell -Command "Get-Process UnrealEditor -ErrorAction SilentlyContinue | Stop-Process -Force"
   powershell -Command '$env:PATH = "D:\Code\UE5\Engine\Binaries\Win64;C:\Program Files\dotnet\;" + $env:PATH; Start-Process -FilePath "D:\Code\UE5\Engine\Binaries\Win64\UnrealEditor.exe" -ArgumentList "D:\UEProject\SharpDemo\SharpDemo.uproject","LV_OpenWorld","-game","-log"; Write-Host launched'
   ```
4. `grep -n "Pal3Boot\|threw\|SMOKE\|Game resources initialized" Saved/Logs/SharpDemo.log | grep -v Mono`
- 注意：**PATH 里 `dotnet` 项必须带尾斜杠**（`C:\Program Files\dotnet\;`），否则 UnrealSharp 找不到 .NET SDK 弹模态框卡死。

## 3. 移植背景与双运行时线
- 把开源 Unity C# 版《仙剑奇侠传三》（`D:\Code\Pal3.Unity`）通过 UnrealSharp 移植到 UE5。
- 游戏数据在 `E:\Program Files (x86)\Steam\steamapps\common\PAL3\`（可用 `PAL3_DATA_PATH` 环境变量覆盖）。
- **插件子模块有两条运行时线，切分支时父工程 TFM/global.json 必须跟着切**：

  | 插件分支 | 运行时 | 父工程 TFM | global.json | 打包命令（BuildAndroid.bat 自动探测） |
  |---|---|---|---|---|
  | **Mono202610**（当前） | Mono（`bUseMono=true`，编辑器+打包统一；Android JIT / iOS INTERP+AOT） | net10.0 | 10.0.201 | `PackageProjectMono` |
  | MobileClr202609 | CoreCLR .NET 11（raw coreclr PAK 解压，仅 mobile 定制；桌面=上游 hostfxr） | net11.0 | 11.0.100-rc.1 | `PackageProjectMobile` |

- 两条线的切换 = 插件子模块切分支 + `Script/*.csproj` 的 `<TargetFramework>` 与 `Script/global.json` 同步改（BuildAndroid.bat 靠 `PackageProjectMono.cs` 是否存在自动选 SDK 与打包命令）。运行时选择由 `DefaultEngine.ini [UnrealSharp] bUseMono` 控制，**只允许写在 DefaultEngine.ini**（UBT 读平台层级、UAT 发布只读基础层，平台级覆盖会造成编译/发布撕裂）。
- `-game` 不重编译 managed 代码，靠手动拷贝 DLL 到 `Binaries/Managed/<tfm>/`。

## 4. 打包测试（聚焦 Mobile）
- Android 打包：`cmd //c "D:\UEProject\SharpDemo\BuildAndroid.bat" Development`（后台 + 轮询 log；步骤标记 `[ OK ] Step N` / `[FAIL]`；硬超时 40 分钟，日志停滞 3 分钟视为死锁）。**必须走 bat 本体**——Git Bash 直启 RunUAT 会丢 PATH 尾反斜杠，cooker 找不到 dotnet SDK（UnrealSharp 重试 5 次 abort，见 KB `ue-android-gradle-agp-and-gitbash-path`）；已完成的阶段用 `--skip-editor` 跳。引擎模板 gradle wrapper 已升 8.13（AGP 8.13 要求）。
- 桌面平台**不折腾**：跟随所选插件分支的上游行为（Mono 线=编辑器也跑 Mono；MobileClr 线=hostfxr）。Win64 PAK 实验记录见 KB `D:\AiAgentKB\ue-win64-pak-bcl-raw-coreclr\README.md`。
- 成功标记（按线区分，设备 logcat / 运行日志）：
  - **Mono 线**：`[Mono] Runtime initialized: ...` → `[Mono] UFS-loaded assembly`（多条）→ `Successfully loaded plugin` → Pal3Boot 标记 + `[Pal3Boot] SMOKE PASS`；崩溃诊断看 `files/mono_stderr.log`（stderr 重定向）。
  - **MobileClr 线**：`[CoreClr] Extracted N, skipped M ...` → `coreclr_initialize => 0x0` → `Discovered N load order manifests` → Pal3Boot + `[Pal3Boot] SMOKE PASS`。
- 机制：**Mono 线**——BCL NonUFS 散放（`MonoSDK/Android/runtime/`，257 个 dll）、项目/插件 DLL UFS 进 PAK（`Content/Managed/Android/`，14 个 + load order json），运行时 preload hook 先查 `Saved/Managed/<plat>` 热更目录再读 PAK。**MobileClr 线**——共享框架 managed+native dll 全部 UFS 进 PAK，boot 解压到 `Saved/Managed/Android/`，coreclr 从解压目录取用，TPA 按 PE CLR header 只收 managed。
