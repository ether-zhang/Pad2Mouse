# DS2Mouse

把 PlayStation 5 的 DualSense 手柄当作鼠标和键盘使用。Windows 桌面工具，常驻系统托盘，全屏游戏中自动让路（白名单可控）。

## 功能

- **左摇杆 → 鼠标移动**（径向死区 + 指数曲线，灵敏度可调）
- **右摇杆 → 滚轮**（垂直方向，速度可调，平滑积分避免抖动）
- **按键映射**（默认）：
  - R2 / L2 扳机：鼠标左键 / 右键（按住，可拖拽）
  - ✕ Cross：鼠标左键单击
  - ○ Circle：鼠标右键单击
  - □ Square：鼠标中键
  - △ Triangle：Enter
  - 方向键：上下左右箭头键
  - PS 键：切换映射启停（按一次禁用，再按一次恢复）
- **全屏自动让路**：检测到 D3D 独占全屏 / 演示模式时自动停用映射；通过进程名白名单允许例外（看视频 / 看 PDF / 浏览器全屏视频等）
- **系统托盘**：右键菜单可启停 / 显示窗口 / 退出；双击托盘打开窗口
- **配置持久化**：所有设置保存到与可执行文件同目录的 `config.json`

## 系统要求

- Windows 10 / 11 (x64)
- .NET 9 SDK（构建用）；运行时只需 .NET Desktop Runtime 9（或随单文件发布自带）
- DualSense 手柄（USB 或蓝牙）

## 连接方式

- **USB 有线**：插上即认（延迟最低）。
- **蓝牙**：先在 Windows 蓝牙设置里把手柄按对码后连接（手柄长按 PS + Create 按键进入配对）。

> 程序首次连接蓝牙时会发送一次 feature report 0x05 探测，把手柄切换到完整报告模式。

## 构建与运行

```powershell
# 构建（Debug）
dotnet build src/DS2Mouse/DS2Mouse.csproj

# 直接运行
dotnet run --project src/DS2Mouse/DS2Mouse.csproj

# 发布（单文件，依赖系统已装的 .NET 9 桌面运行时）
dotnet publish src/DS2Mouse/DS2Mouse.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

# 发布（自带运行时，体积约 100MB）
dotnet publish src/DS2Mouse/DS2Mouse.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

发布后生成的 `DS2Mouse.exe` 双击即可运行；配置文件 `config.json` 会出现在同目录。

## 配置文件示例 (`config.json`)

```json
{
  "Enabled": true,
  "LeftStick": {
    "Deadzone": 0.10,
    "Sensitivity": 12.0,
    "Exponent": 2.0
  },
  "RightStick": {
    "Deadzone": 0.20,
    "Speed": 8.0,
    "InvertVertical": false
  },
  "TriggerThreshold": 0.20,
  "FullscreenWhitelist": ["chrome", "mpv", "vlc"]
}
```

- 白名单条目按进程名匹配（不区分大小写，`.exe` 后缀可省略）。
- 主界面下方的 "Foreground" 实时显示当前前台进程名 — 全屏游戏弹出时把那个名字加进去就能例外。

## 已知限制

- `SendInput` 不能控制以更高完整性等级运行的进程（如管理员模式下的反作弊游戏）。这是 Windows 安全设计，无法绕过。
- 全屏检测使用 `SHQueryUserNotificationState`，识别 D3D 独占全屏与 PowerPoint 演示模式。**无边框全屏游戏（多数现代游戏）不会被识别** —— 这种情况下使用 PS 键手动切换映射。
- 单手柄。多手柄识别为后续增强。

## 项目结构

```
src/DS2Mouse/
├── App.xaml(.cs)              # 应用入口、托盘、生命周期
├── MainWindow.xaml(.cs)       # 简易设置窗口
├── Models/
│   ├── AppConfig.cs           # 配置数据模型
│   └── DualSenseState.cs      # 手柄帧
├── Services/
│   ├── DualSenseReader.cs     # HidSharp + USB/BT 报告解析
│   ├── InputSimulator.cs      # SendInput 封装
│   ├── MapperEngine.cs        # 摇杆/按键 → 输入动作（125Hz tick）
│   ├── FullscreenGuard.cs     # SHQueryUserNotificationState 轮询
│   ├── ForegroundProcess.cs   # 取前台进程名
│   └── ConfigStore.cs         # JSON 加载 / 保存
└── Resources/tray.ico         # 托盘图标
```

## 开发笔记

- 提交按里程碑 M1 → M8 组织，参见 `git log`。
- 单元测试目前未配置；映射逻辑通过手柄实物验证。后续如要扩展为多手柄 / 自定义映射，建议先抽出 `IInputSink` 接口便于注入测试替身。
