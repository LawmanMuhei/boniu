# 波妞摸鱼：Windows 抖音摸鱼小窗口

波妞摸鱼是一款开源、轻量的 Windows 抖音桌面小窗工具。它基于 Microsoft Edge WebView2，支持窗口置顶、老板键快速隐藏、鼠标移出自动隐藏、清爽模式、直播横屏、登录状态保存，以及 x64 / x86 Windows。

项目适合需要“抖音摸鱼小窗口”“Windows 抖音悬浮窗”“抖音老板键”或轻量抖音桌面客户端的用户。程序最终交付为单个 EXE；首次运行会把组件解压到 `%LOCALAPPDATA%\MiniViewWebView2\App\1.7.0-<架构>`，以后直接复用。抖音网页由系统 WebView2 Runtime 渲染，不携带完整 Chromium/Electron。

## 下载

- [GitHub Releases 下载页面](https://github.com/LawmanMuhei/boniu/releases/latest)
- 64 位 Windows：`BoniuMoyu.exe`
- 32 位 Windows：`BoniuMoyu-x86.exe`

每个 EXE 都提供配套 `.sha256` 文件，可用于校验下载完整性。程序暂未配置商业代码签名证书，因此 Windows 可能显示“未知发布者”。

## 功能

- 只允许 HTTPS 抖音域名导航，登录 Cookie 保存在 `%LOCALAPPDATA%\MiniViewWebView2\UserData`。
- 默认 `Ctrl + Alt + D` 隐藏/显示程序，隐藏后无任务栏按钮，鼠标移回原位置不会恢复。
- 鼠标离开窗口并达到设置的延迟后，先立即隐藏，再在后台静音并暂停视频，不等待网页响应。
- 设置中可以关闭鼠标移出隐藏，或选择 0.1、0.3、0.5 秒延迟。
- 可选择切换到其他程序时自动隐藏；Windows 锁屏、断开本地或远程会话时强制隐藏。
- 默认 `Ctrl + Alt + B` 彻底隐藏/恢复顶部工具栏。
- 默认 `Ctrl + Alt + M` 静音/恢复声音。
- 默认 `Ctrl + Alt + F` 进入/退出清爽模式，只隐藏页面装饰并保留抖音原生播放器布局。
- 设置中可以分别录入“隐藏/显示程序”“隐藏/显示边框”和“进入/退出清爽模式”组合键；与现有快捷键冲突、被其他程序占用或属于系统保留的组合键不会保存。
- 开启直播自动横屏后，URL 直播与推荐页手动打开的直播弹层都会切换横屏。
- 直播时隐藏顶部主播栏、底部礼物栏、右侧聊天区，并扩展播放器。
- 普通窗口和直播窗口分别保存最后的位置与大小。
- 设置中可以打开本地数据目录、恢复程序默认设置，或经二次确认后清除登录和网页缓存。
- 每次启动通过 `LawmanMuhei/boniu` 的 GitHub Releases 检查更新；发现新版本后由用户确认，下载文件必须通过配套 SHA-256 校验。安装前备份设置和旧 EXE，新版本无法健康启动时自动回滚。

## 系统要求

- Windows 10 / Windows 11，支持 64 位和 32 位系统。
- Microsoft Edge WebView2 Runtime。正常更新的 Windows 10/11 和 Microsoft Edge 通常已自带。
- 不需要安装 Node.js、Electron、Visual Studio 或 .NET SDK。

## 构建

在 PowerShell 中运行：

```powershell
.\scripts\build.ps1
.\scripts\build.ps1 -Platform x86
```

脚本使用 Windows 自带的 .NET Framework 4.8 编译器，并在项目内下载 Microsoft WebView2 SDK NuGet 包。64 位输出为 `dist\波妞摸鱼.exe`，32 位输出为 `dist\波妞摸鱼-x86.exe`。两种架构使用各自的 WebView2Loader 和 `%LOCALAPPDATA%\MiniViewWebView2\App\1.7.0-<架构>` 目录。

没有代码签名证书时构建会明确跳过签名。以后取得 PFX 证书后设置 `BONIU_SIGN_PFX` 和 `BONIU_SIGN_PASSWORD`，或设置证书存储指纹 `BONIU_SIGN_THUMBPRINT`，构建脚本会同时签名内层程序与最终 EXE，并使用时间戳服务。发布新版本可运行：

```powershell
.\scripts\publish.ps1 -Version 1.7.0 -Notes '更新说明'
```

发布脚本会上传 x64 的 `BoniuMoyu.exe`、x86 的 `BoniuMoyu-x86.exe` 及各自的 `.sha256`。自动更新会按当前进程架构选择正确资产。需要提前安装 `gh` 并执行 `gh auth login`。当前没有签名证书，因此 SHA-256 与 GitHub HTTPS 可校验下载完整性，但 Windows 仍可能显示“未知发布者”，直到配置正式代码签名证书。

应用图标源文件位于 `assets\boniu-moyu-icon.svg`，构建时使用包含 16 至 256 像素尺寸的 `assets\boniu-moyu.ico`。

## 隐私边界

该程序只让窗口从桌面和任务栏中消失，不能对系统管理员、任务管理器、远程管理、屏幕录制或进程审计隐藏。

## 开源许可

本项目采用 [MIT License](LICENSE) 开源。抖音及其商标、网页内容与服务归相应权利人所有，本项目与抖音官方无关联。
