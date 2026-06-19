# Pad2Mouse

[English](README.en.md) | **简体中文**

把 PlayStation DualSense 或 Xbox 手柄当作鼠标和键盘使用。常驻系统托盘的 Windows 桌面工具，全屏游戏中自动让路，零依赖单文件，双击即用。

适合在沙发上浏览网页、看视频、操作 HTPC，或在手边没有鼠标键盘时控制 Windows 桌面。

## 开箱即用（OOTB）

1. 到 [Releases](https://github.com/ether-zhang/Pad2Mouse/releases) 下载最新的 `Pad2Mouse.exe`。
2. 双击运行 —— **无需安装**，不依赖 .NET 运行时、Visual C++ 运行库或任何驱动，也不需要管理员权限。
3. 程序常驻系统托盘；接上 DualSense 或 Xbox 手柄即可用左摇杆控制鼠标。

> 发布的 EXE 是自包含单文件，首次启动会在 `%LOCALAPPDATA%\Microsoft\.NET` 下解压运行时，略有 1~2 秒延迟，之后启动即时。
>
> 由于未做代码签名，首次运行可能弹出 Windows SmartScreen 提示「Windows 已保护你的电脑」——点击 **更多信息 → 仍要运行** 即可。

## 功能

### 指针与滚动
- **左摇杆 → 鼠标移动**：径向死区 + 指数曲线 + 时间线性加速，按住越久越快，回到死区即归零。
- **右摇杆 → 滚轮**：垂直滚动，速度可调，亚像素累加避免抖动。

### 按键映射
默认映射如下，**八个按键均可在主界面下拉自由修改**（也可绑定为某个键盘按键的「按住式」映射）：

| 按键 (PS / Xbox) | 默认动作 |
| --- | --- |
| L2 / LT | Ctrl |
| R2 / RT | 鼠标左键按住 |
| ✕ Cross / A | 鼠标左键按住 |
| ○ Circle / B | 鼠标右键单击 |
| □ Square / X | 鼠标中键单击 |
| △ Triangle / Y | Enter |
| L3 / LSB | Tab |
| R3 / RSB | 鼠标中键单击 |
| 方向键 / D-Pad | 上下左右方向键（固定） |

> 鼠标左键按住支持拖拽；R2 与 ✕ 任一按下都算左键按住，松开任一不影响另一者。

组合键（固定）：
- **L1 + R1 / LB + RB**：呼出 / 收起内置虚拟键盘
- **Create + Options / View + Menu**：把鼠标光标瞬移回主显示器中心（多屏走丢光标时的救命键）
- **L3 + R3 / LSB + RSB**：随时启用 / 暂停映射，不必回界面切换

### 多手柄并发
- DualSense（含 DualSense Edge）走 HID，Xbox（含兼容 XInput 的第三方手柄）走 XInput。
- USB + 蓝牙、PS + Xbox 可同时连接，所有手柄并发驱动同一光标。
- 连接显示为 `DualSense Edge(蓝牙)` 这种紧凑格式，多设备一目了然。

### 内置虚拟键盘
- L1 + R1 唤出半透明 QWERTY 键盘，用摇杆移动光标点选，无需起身够物理键盘。

### Win11 系统手柄导航抑制
- 屏蔽 Windows 11 系统应用（设置、开始菜单等）自带的手柄焦点导航，避免与本工具的映射「双重输入」。
- 实现为低级键盘钩子，只拦截系统注入的导航键（方向键 / Tab / Enter / Esc / Space / 翻页 / Home / End），且**仅在手柄实际活动的瞬间**生效——因此不影响远程串流（Sunshine / Parsec / RDP）注入的键盘输入，也不影响罗技侧键、AutoHotkey 等其它注入来源。

### 全屏自动让路
- 检测到前台窗口铺满整个显示器（独占全屏 / 无边框全屏 / D3D / 演示模式 / PowerPoint 等）即停用映射；进程白名单允许例外。最大化窗口、空桌面不算全屏。

### 界面与系统集成
- 深色现代化界面，主题色随手柄类型变化：DualSense 蓝、Xbox 绿、双连蓝绿渐变。
- 简体中文 / English 运行时即时切换（默认 English），托盘菜单跟随。
- 常驻托盘：右键菜单可启停 / 显示窗口 / 退出，双击托盘打开；启停时弹 Windows 通知。
- 可选开机自启（写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）。
- 所有设置保存到 `%LOCALAPPDATA%\Pad2Mouse\config.json`，每次改动即时落盘；旧版（写在 EXE 同目录）配置首次启动自动迁移。

## 系统要求

- Windows 10（1809 及以上）/ Windows 11，x64
- 运行：自包含单文件 EXE，无需额外安装 .NET 运行时、Visual C++ 运行库或任何驱动
- 构建：.NET 9 SDK
- 一只 DualSense 或 Xbox 手柄（USB 或蓝牙）

## 连接方式

- **USB 有线**：插上即认，延迟最低。
- **蓝牙**：DualSense 长按 PS + Create 进入配对，Xbox 长按配对键进入配对，然后在 Windows 蓝牙设置中连接。

> DualSense 蓝牙首次连接会请求 feature report 0x05，把手柄切换到完整报告模式（0x31）。

## 构建与运行

```powershell
# Debug 构建（IDE 调试用）
dotnet build src/DS2Mouse/DS2Mouse.csproj

# 直接运行
dotnet run --project src/DS2Mouse/DS2Mouse.csproj

# 发布：自包含单文件 EXE（目标机器零依赖）
# Release 配置在 csproj 里已开启 PublishSingleFile / 内嵌 PDB / DeterministicSourcePaths，
# 命令行只需补 runtime + self-contained + 原生库内嵌。
dotnet publish src/DS2Mouse/DS2Mouse.csproj `
  -c Release -r win-x64 `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

产物：`src/DS2Mouse/bin/Release/net9.0-windows/win-x64/publish/Pad2Mouse.exe`。拷到目标机器双击即用；`config.json` 自动写入 `%LOCALAPPDATA%\Pad2Mouse\`。

## 配置文件示例 (`config.json`)

```json
{
  "Enabled": true,
  "Language": "en",
  "EnableNotifications": true,
  "LeftStick": {
    "Deadzone": 0.10,
    "Sensitivity": 12.0,
    "Exponent": 2.0,
    "AccelMaxFactor": 2.5,
    "AccelRampSeconds": 1.0
  },
  "RightStick": {
    "Deadzone": 0.20,
    "Speed": 8.0,
    "InvertVertical": false,
    "AccelMaxFactor": 2.5,
    "AccelRampSeconds": 1.0
  },
  "TriggerThreshold": 0.20,
  "Mappings": {
    "L2": "Ctrl",
    "R2": "LeftHold",
    "Cross": "LeftHold",
    "Circle": "RightClick",
    "Square": "MiddleClick",
    "Triangle": "Enter",
    "L3": "Tab",
    "R3": "MiddleClick"
  },
  "FullscreenWhitelist": ["chrome", "mpv", "vlc"]
}
```

- `Language`：`en` 或 `zh-CN`。
- 映射动作支持：`LeftHold` / `RightHold` / `LeftClick` / `RightClick` / `MiddleClick` / `Ctrl` / `Shift` / `Enter` / `Tab` 等，以及 `Key:0xNN`（绑定到某个虚拟键的按住式映射，可在界面通过虚拟键盘点选生成）。
- `AccelMaxFactor` = 1.0 等于关闭加速；`AccelRampSeconds` 是从 1× 线性升到 `AccelMaxFactor` 所需秒数。
- 白名单条目按进程名匹配（不区分大小写，`.exe` 后缀可省略）。主界面下方「前台进程」实时显示当前前台进程名 —— 想例外就把那个名字加进去。

## 已知限制

- `SendInput` 无法控制以更高完整性级别运行的进程（如以管理员权限运行的窗口）。这是 Windows 安全设计，无法绕过。基于内核反作弊（VAC / EAC / BattlEye 等）的在线竞技游戏请关闭本工具，或用全屏白名单让其自动让路。
- 全屏判定按「窗口矩形 = 显示器矩形」严格相等：自动隐藏任务栏 + 最大化窗口会被判为全屏（体验上接近全屏，属预期行为）。
- 同一只 DualSense 同时经 USB 和蓝牙在线时，连接列表偶尔仍会出现两条 —— 现有按 HID 序列号去重的逻辑在某些时序下未生效，待修。

## 项目结构

```
src/DS2Mouse/
├── App.xaml(.cs)                 # 应用入口、托盘、生命周期、主题色、本地化加载
├── MainWindow.xaml(.cs)          # 主页 + 设置 两个 tab
├── OnScreenKeyboardWindow.xaml   # 半透明内置虚拟键盘
├── Models/
│   ├── AppConfig.cs              # 配置数据模型
│   ├── ButtonActions.cs          # 可选映射动作 + 默认按键映射
│   └── DualSenseState.cs         # 归一化的手柄帧
├── Services/
│   ├── IControllerReader.cs      # 读取器抽象
│   ├── ControllerSource.cs       # 聚合 DualSense + XInput 两个读取器
│   ├── DualSenseReader.cs        # HidSharp + USB/BT 报告解析
│   ├── XInputReader.cs           # Xbox 手柄轮询
│   ├── StateMerge.cs             # 多手柄帧合并
│   ├── InputSimulator.cs         # SendInput 封装（带 sentinel 标记）
│   ├── MapperEngine.cs           # 摇杆/按键 → 输入动作（125Hz tick，含加速）
│   ├── ShellInputSuppressor.cs   # 低级键盘钩子，抑制 Win11 系统手柄导航
│   ├── FullscreenGuard.cs        # 窗口矩形 + SHQuery 双重检测
│   ├── LocalizationService.cs    # ResourceDictionary 热切换
│   ├── StartupRegistration.cs    # 开机自启注册表项
│   └── ConfigStore.cs            # JSON 加载 / 保存 / 迁移
└── Resources/
    ├── Theme.xaml                # 深色主题 + 控件样式
    ├── Strings.zh-CN.xaml        # 中文资源
    ├── Strings.en.xaml           # 英文资源
    └── tray.ico                  # 托盘 / 任务栏 / EXE 图标
```

## 依赖

- [HidSharp](https://www.nuget.org/packages/HidSharp) — DualSense HID 读取
- [Hardcodet.NotifyIcon.Wpf](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf) — 系统托盘图标

Xbox 手柄通过 Windows 自带的 XInput 读取，无额外依赖。

## License

MIT，详见 [LICENSE](LICENSE)。
