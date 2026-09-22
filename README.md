# 波妞摸鱼：Windows 抖音摸鱼小窗口

波妞摸鱼是一款开源、轻量的 Windows 抖音桌面小窗工具。它基于 Microsoft Edge WebView2，支持窗口置顶、老板键快速隐藏、鼠标移出自动隐藏、清爽模式、直播横屏、登录状态保存，以及 x64 / x86 Windows。

项目适合需要“抖音摸鱼小窗口”“Windows 抖音悬浮窗”“抖音老板键”或轻量抖音桌面客户端的用户。程序最终交付为单个 EXE；首次运行会把组件解压到 `%LOCALAPPDATA%\MiniViewWebView2\App\1.8.0-<架构>`，以后直接复用。抖音网页由系统 WebView2 Runtime 渲染，不携带完整 Chromium/Electron。

## 下载

> [!IMPORTANT]
> **本程序仅支持 Windows 10 / Windows 11，不支持 Windows 7、Windows 8 或 Windows 8.1。** 请勿在 Windows 7 上下载或运行。

- [GitHub Releases 下载页面](https://github.com/LawmanMuhei/boniu/releases/latest)
- 64 位 Windows：`BoniuMoyu.exe`
- 32 位 Windows：`BoniuMoyu-x86.exe`

每个 EXE 都提供配套 `.sha256` 文件，可用于校验下载完整性。程序暂未配置商业代码签名证书，因此 Windows 可能显示“未知发布者”。

## 功能

- 只允许 HTTPS 抖音域名导航，登录 Cookie 保存在 `%LOCALAPPDATA%\MiniViewWebView2\UserData`。
- 默认 `Ctrl + Alt + D` 隐藏/显示程序，隐藏后无任务栏按钮，鼠标移回原位置不会恢复。
- 鼠标离开窗口缓冲区并达到设置的延迟后，先立即隐藏，再在后台静音并暂停视频；按住或拖动鼠标期间不会误隐藏。
- 设置中可以关闭鼠标移出隐藏，或选择 0.1、0.3、0.5 秒延迟。
- 可选择切换到其他程序时自动隐藏；Windows 锁屏、断开本地或远程会话时隐藏，即使快捷键不可用也可通过重新打开程序或托盘恢复。
- 设置中可开启托盘恢复入口；恢复快捷键冲突时会自动保留托盘入口。双击恢复，菜单可恢复工具栏并打开设置。
- 隐藏约 10 秒后尝试挂起 WebView；运行时拒绝时稍后重试，恢复时唤醒。
- 默认 `Ctrl + Alt + B` 彻底隐藏/恢复顶部工具栏。
- 默认 `Ctrl + Alt + M` 静音/恢复声音。
- 默认 `Ctrl + Alt + F` 进入/退出清爽模式，只隐藏页面装饰并保留抖音原生播放器布局。
- 清爽布局健康检查异常时回退原始布局并显示提示，可点击提示或设置中的“重新应用页面样式”重试。
- 设置中可以分别录入“隐藏/显示程序”“隐藏/显示边框”“静音/恢复声音”和“进入/退出清爽模式”组合键，也可按 Del 禁用；冲突、被占用或属于系统保留的组合键不会保存。
- 开启直播自动横屏后，URL 直播与推荐页手动打开的直播弹层都会切换横屏。
- 直播时隐藏顶部主播栏、底部礼物栏、右侧聊天区，并扩展播放器。
- 普通窗口和直播窗口分别保存最后的位置与大小。
- 设置中可以打开本地数据目录、恢复程序默认设置，或经二次确认后清除登录和网页缓存。
- 每次启动通过 `LawmanMuhei/boniu` 的 GitHub Releases 检查更新；发现新版本后由用户确认，下载文件必须通过配套 SHA-256 校验。安装前备份设置和旧 EXE，新版本无法健康启动时自动回滚。

## 系统要求

- Windows 10 / Windows 11，支持 64 位和 32 位系统。
- 不支持 Windows 7、Windows 8 或 Windows 8.1，也不提供这些系统的兼容版本。
- Microsoft Edge WebView2 Runtime。正常更新的 Windows 10/11 和 Microsoft Edge 通常已自带。
- 不需要安装 Node.js、Electron、Visual Studio 或 .NET SDK。

## 构建

在 PowerShell 中运行：

```powershell
.\scripts\build.ps1
.\scripts\build.ps1 -Platform x86
```

脚本使用 Windows 自带的 .NET Framework 4.8 编译器，并在项目内下载 Microsoft WebView2 SDK NuGet 包。64 位输出为 `dist\波妞摸鱼.exe`，32 位输出为 `dist\波妞摸鱼-x86.exe`。两种架构使用各自的 WebView2Loader 和 `%LOCALAPPDATA%\MiniViewWebView2\App\1.8.0-<架构>` 目录。

版本唯一来源是 `src/AppVersion.cs`；构建前会核对程序集、启动器、清单、README 和使用说明的一致性。未发布的本轮优化沿用源码版本，不表示 GitHub 已发布这些变更。

没有代码签名证书时构建会明确跳过签名。以后取得 PFX 证书后设置 `BONIU_SIGN_PFX` 和 `BONIU_SIGN_PASSWORD`，或设置证书存储指纹 `BONIU_SIGN_THUMBPRINT`，构建脚本会同时签名内层程序与最终 EXE，使用时间戳服务并验证签名。需要强制签名时可传 `-RequireSigning`，缺少证书即失败；不会自动购买或获取证书。发布新版本可运行：

```powershell
.\scripts\publish.ps1 -Version 1.8.0 -Notes '更新说明'
```

发布脚本会上传 x64 的 `BoniuMoyu.exe`、x86 的 `BoniuMoyu-x86.exe` 及各自的 `.sha256`。自动更新会按当前进程架构选择正确资产。需要提前安装 `gh` 并执行 `gh auth login`。当前没有签名证书，因此 SHA-256 与 GitHub HTTPS 可校验下载完整性，但 Windows 仍可能显示“未知发布者”，直到配置正式代码签名证书。

应用图标源文件位于 `assets\boniu-moyu-icon.svg`，构建时使用包含 16 至 256 像素尺寸的 `assets\boniu-moyu.ico`。

## 验证与维护

```powershell
.\scripts\check-version.ps1
.\scripts\test.ps1 -Platform x64
.\scripts\test.ps1 -Platform x86
```

测试构建隔离输出到 `artifacts/verification/`，不覆盖现有发布 EXE，不签名、不发布、不安装更新。GUI 测试使用临时独立用户目录，不注册全局快捷键，不读写真实登录与设置；会短暂显示测试窗口。

测试包括离线更新传输、自测、WebView 冒烟、直播布局、设置缩放及隐藏恢复回归。结果为 `tests-<架构>.json` 和 `regression-<架构>.json`。测试失败或超时返回非零退出码。

回归报告记录模拟视频播放、隐藏、可用时的挂起、恢复和多轮切换后的 CPU/内存及延迟。CPU 包含测试宿主与独立 WebView 进程，按整机逻辑核数归一化；工作集可能重复计算共享页。**模拟页面不代表真实抖音解码、网络和长期内存表现，也不是修改前后的性能提升证明。**真实页面、真实音频、多显示器迁移、系统锁屏、托盘交互及实际更新安装仍需人工验收。

源码按职责划分：`MainForm.Lifecycle.cs`（窗口状态）、`MainForm.Page.cs` / `PageScripts.cs`（网页适配）、`MainForm.Settings.cs`（设置视图）、`MainForm.Hotkeys.cs`（快捷键）、`MainForm.Recovery.cs`（托盘）、`UiControls.cs`（自绘控件）。详细变更与验收边界见 `docs/mcp-optimization-implementation.md`。

## 隐私边界

该程序只让窗口从桌面和任务栏中消失，不能对系统管理员、任务管理器、远程管理、屏幕录制或进程审计隐藏。

## 开源许可

本项目采用 [MIT License](LICENSE) 开源。抖音及其商标、网页内容与服务归相应权利人所有，本项目与抖音官方无关联。
