# DS2Mouse

把 PlayStation 5 的 DualSense 手柄当作鼠标和键盘使用。Windows 桌面工具，常驻系统托盘，全屏游戏中自动让路（白名单可控）。

## 功能

- **左摇杆 → 鼠标移动**：径向死区 + 指数曲线 + 时间线性加速。按住一段时间后速度自动变快，回到死区即归零。
- **右摇杆 → 滚轮**：垂直方向，速度可调，分数累加积分避免抖动。
- **按键映射**（默认）：
  - R2 扳机 / ✕ Cross：**鼠标左键按住**（任一按下都算，松开任一不影响另一者，支持拖拽）
  - L2 扳机：鼠标右键按住（可拖拽）
  - ○ Circle：鼠标右键单击
  - □ Square：鼠标中键单击
  - △ Triangle：Enter
  - 方向键：上下左右箭头键
  - PS 键：切换映射启停
- **全屏自动让路**：检测到任意前台窗口铺满整个显示器（独占全屏 / 无边框全屏 / D3D / 演示模式都涵盖）即停用映射；通过进程名白名单允许例外。最大化窗口、空桌面不算全屏。
- **系统托盘**：右键菜单可启停 / 显示窗口 / 退出；双击托盘打开窗口。启停切换时弹 Windows 通知。
- **多语言**：中文 / English，运行时即时切换，托盘菜单跟随。
- **配置持久化**：所有设置保存到 `%LOCALAPPDATA%\Pad2Mouse\config.json`，每次改动即时落盘。旧版（写在 exe 同目录）的配置首次启动会自动迁移。

## 系统要求

- Windows 10 / 11 (x64)
- 自包含发布 EXE：无依赖，双击即用
- 源码构建：.NET 9 SDK
- DualSense 手柄（USB 或蓝牙）

## 连接方式

- **USB 有线**：插上即认（延迟最低）。
- **蓝牙**：先在 Windows 蓝牙设置里把手柄按对码后连接（手柄长按 PS + Create 按键进入配对）。

> 程序首次连接蓝牙时会请求 feature report 0x05，把手柄切换到完整报告模式（0x31）。

## 构建与运行

```powershell
# Debug 构建（IDE 调试用）
dotnet build src/DS2Mouse/DS2Mouse.csproj

# 直接运行
dotnet run --project src/DS2Mouse/DS2Mouse.csproj

# 发布：自包含单文件 EXE（目标机器零依赖，~170 MB）
# Release 配置在 csproj 里已开启 PublishSingleFile / 内嵌 PDB / DeterministicSourcePaths，
# 命令行只需补 runtime + self-contained + 原生库内嵌。
dotnet publish src/DS2Mouse/DS2Mouse.csproj `
  -c Release -r win-x64 `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

产物：`src/DS2Mouse/bin/Release/net9.0-windows/win-x64/publish/Pad2Mouse.exe`。拷到目标机器双击即用；`config.json` 自动写入 `%LOCALAPPDATA%\Pad2Mouse\`。

### Steam 上传

把上面产出的 `Pad2Mouse.exe` 拷到 Steamworks SDK 的 `sdk\tools\ContentBuilder\content\`，VDF 脚本在 `sdk\tools\ContentBuilder\scripts\app_build_4798270.vdf`（脚本里 `SetLive ""` 表示上传后不会自动发布到任何分支，需要去后台手动 Set Build Live）：

```powershell
cd <SDK>\sdk\tools\ContentBuilder
.\builder\steamcmd.exe `
  +login <你的Steamworks账号> `
  +run_app_build "scripts\app_build_4798270.vdf" `
  +quit
```

首次会要密码 + Steam Guard 2FA，token 缓存约 30 天。

## 配置文件示例 (`config.json`)

```json
{
  "Enabled": true,
  "Language": "zh-CN",
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
    "InvertVertical": false
  },
  "TriggerThreshold": 0.20,
  "FullscreenWhitelist": ["chrome", "mpv", "vlc"]
}
```

- `Language`：`zh-CN` 或 `en`。
- `AccelMaxFactor` = 1.0 等于关闭加速；`AccelRampSeconds` 是从 1× 线性升到 `AccelMaxFactor` 所需秒数。
- 白名单条目按进程名匹配（不区分大小写，`.exe` 后缀可省略）。
- 主界面下方的 "前台进程" 实时显示当前前台进程名 —— 想例外就把那个名字加进去。

## 已知限制

- `SendInput` 不能控制以更高完整性等级运行的进程（如管理员模式下的反作弊游戏）。这是 Windows 安全设计，无法绕过。
- 全屏判定按"窗口矩形 = 显示器矩形"严格相等：自动隐藏任务栏 + 最大化窗口会被判为全屏；这是预期行为（用户体验上接近全屏）。
- 单手柄。多手柄识别为后续增强。

## 项目结构

```
src/DS2Mouse/
├── App.xaml(.cs)              # 应用入口、托盘、生命周期、本地化加载
├── MainWindow.xaml(.cs)       # 主页 + 设置 两个 tab
├── Models/
│   ├── AppConfig.cs           # 配置数据模型
│   └── DualSenseState.cs      # 手柄帧
├── Services/
│   ├── DualSenseReader.cs     # HidSharp + USB/BT 报告解析
│   ├── InputSimulator.cs      # SendInput 封装
│   ├── MapperEngine.cs        # 摇杆/按键 → 输入动作（125Hz tick，含加速）
│   ├── FullscreenGuard.cs     # 窗口矩形 + SHQuery 双重检测
│   ├── LocalizationService.cs # ResourceDictionary 热切换
│   └── ConfigStore.cs         # JSON 加载 / 保存
└── Resources/
    ├── tray.ico               # 托盘 / 任务栏 / EXE 图标
    ├── Strings.zh-CN.xaml     # 中文资源
    └── Strings.en.xaml        # 英文资源
```

## 开发笔记

- 提交按里程碑 M1 → M8 组织（参见 `git log`），后续按特性提交。
- 单元测试目前未配置；映射逻辑通过手柄实物验证。后续如要扩展为多手柄 / 自定义映射 GUI，建议先抽出 `IInputSink` 接口便于注入测试替身。
