# 项目功能分析：波妞摸鱼（Boniu Moyu）

分析日期：2026-09-19；分析范围：整个仓库（D:/Cline/douyin-mini-webview2）。

## 1. 项目定位

波妞摸鱼是一个轻量级 WebView2 原生 Windows 小窗程序，用系统自带的 Microsoft Edge WebView2 Runtime 渲染抖音网页，最终交付为单个 EXE。版本 1.6.0。不携带 Chromium/Electron，不收集或上传用户数据。

## 2. 技术栈与构建

- C# / .NET Framework 4.8 / WinForms + Microsoft.Web.WebView2 SDK。
- scripts/build.ps1 使用 Windows 自带的 .NET Framework 4.8 编译器构建，并在项目内下载 WebView2 NuGet 包，无需 Visual Studio 或 .NET SDK；输出 dist/波妞摸鱼.exe。
- app.config 固定 .NETFramework 4.8；app.manifest 声明 asInvoker 权限与 PerMonitorV2/PerMonitor DPI 感知。
- 无 .git 仓库、无 .csproj（直接以源码 + 脚本方式管理）。

## 3. 目录结构

| 路径 | 说明 |
|---|---|
| src/Program.cs (146 行) | 入口：单实例 Mutex、第二实例显示窗口事件、全局异常捕获、--self-test 自检 |
| src/Bootstrap.cs (126 行) | 单 EXE 引导器：把内置资源解压到 %LOCALAPPDATA%\MiniViewWebView2\App\1.6.0-x64 后启动真实程序 |
| src/MainForm.cs (2651 行) | 主窗口：WebView2 集成、导航守卫、热键、自动隐藏、直播增强、设置面板、自绘控件 |
| src/Logic.cs (265 行) | WindowRules（URL 校验、缩放、隐藏延迟、横屏计算）与 HotkeyDefinition（快捷键解析/冲突/系统保留键） |
| src/SettingsStore.cs (146 行) | AppSettings 的 JSON 持久化（settings.json） |
| src/Diagnostics.cs (133 行) | 运行日志，写入 %LOCALAPPDATA%\MiniViewWebView2\logs |
| scripts/build.ps1 | 构建脚本 |
| assets/ | 应用图标源文件与多尺寸 ico |
| dist/ | 1.6.0 波妞摸鱼.exe 及 1.3.0/1.3.1 历史版本与源码包 |
| build/ | WebView2 各 DLL、payload、波妞摸鱼.Tests.exe |
| README.md / 波妞摸鱼-使用说明.txt | 功能与使用说明 |

## 4. 核心功能

### 4.1 抖音专用浏览与安全边界

- 启动默认打开 https://www.douyin.com/?recommend=1。
- OnNavigationStarting 导航守卫：仅允许 HTTPS 的 douyin.com 及其子域名；拒绝非 HTTPS、拒绝伪造域名（如 douyin.com.example.com）。
- 新窗口请求：抖音域名在当前窗口内导航，其余直接吞掉（Handled=true）。
- WebView2 配置：拒绝全部权限请求（相机/定位等）、取消下载、禁用 DevTools、浏览器快捷键、状态栏与缩放控件；启动参数 --autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling。
- 登录状态由 WebView2 用户数据目录（%LOCALAPPDATA%\MiniViewWebView2\UserData）保存 Cookie/LocalStorage/缓存，程序自身不读取不上传。

### 4.2 全局快捷键（Win32 RegisterHotKey，含 NO_REPEAT）

| 默认键 | 功能 | 可自定义 |
|---|---|---|
| Ctrl+Alt+D | 隐藏/显示程序 | 是 |
| Ctrl+Alt+B | 彻底隐藏/恢复顶部工具栏 | 是 |
| Ctrl+Alt+M | 静音/恢复声音 | 否（固定，也作为冲突基准） |
| Ctrl+Alt+F | 沉浸模式（隐藏页面栏位） | 是 |

- 设置面板内直接按键录入；HotkeyDefinition.TryParse 要求至少一个修饰键（Ctrl/Alt/Shift/Win），拒绝修饰键本身作主键，拒绝系统保留组合（Alt+F4、Ctrl+Alt+Delete、Ctrl+Shift+Escape、Win+L）及与其他快捷键冲突的组合。

### 4.3 自动隐藏

- 10ms 轮询检测鼠标是否在窗口内；离开并达到设定延迟（0.1/0.3/0.5 秒，NormalizeAutoHideDelay 校验，非法值回退 0.1s）后先立即隐藏窗口，再执行 PauseVideosScript 后台暂停所有播放中的视频（记录于 globalThis.__miniViewResumeVideos），恢复时 ResumeVideosScript 重播。
- 窗口无边框、ShowInTaskbar=false，隐藏后从桌面与任务栏消失；鼠标移回原位置不恢复（有 2.2 秒显示宽限期 showGraceUntil，防止刚显示又被隐藏）。
- 可选「失去焦点时隐藏」（WM_ACTIVATEAPP 检测）。
- 强制隐藏：SystemEvents.SessionSwitch 监听 SessionLock / ConsoleDisconnect / RemoteDisconnect，锁屏或断开会话时立即隐藏。

### 4.4 直播增强

- 每 250ms 检测直播状态（liveTimer）：URL 规则（live.douyin.com 或 /live/ 路径）或 DOM 探测（LiveProbeScript 检查 #PlayerLayout 下 __livingPlayer__ / data-e2e=living-container）。
- 「直播自动横屏」：CalculateLandscapeBounds 计算 16:9（最小 640x360）、以当前窗口中心定位并钳制在工作区内；退出直播恢复普通窗口；普通与直播窗口位置/大小分别记忆。
- 「直播纯画面」：探测脚本同时执行样式注入，display:none 隐藏 HeaderLayout（主播栏）、GiftMenuLayout/BottomLayout（礼物栏）、RightPanelLayout/RightBackgroundLayout（聊天区），并把 ContainerBackgroundLayout/LeftBackgroundLayout/PlayerLayout 拉伸为 100% 占满。
- LiveSmokeTestHtml 用模拟 DOM 验证上述选择器规则（--live-smoke-test）；抖音网页结构变化时可用 --dom-probe 导出 DOM 报告重新适配（DomProbeScript：video 元素、18 级祖先、data-e2e 元素、四边采样点）。

### 4.5 界面与交互

- 无边框圆角窗口：Win11 用 DWMWA_WINDOW_CORNER_PREFERENCE，旧系统用 GraphicsPath Region 回退（dwmRoundsCorners 探测）。
- 36px 顶部工具栏：后退、刷新、置顶/取消置顶、静音、设置、隐藏边框、立即隐藏、退出 + 状态圆点（Ready/Loading/Error）；拖动顶部空白移动窗口，WM_NCHITTEST 实现边缘调整大小。
- 深色主题（UiPalette，iOS 风格绿色强调色 #30D158）；自绘控件：RoundedButton、ToggleSwitch、SegmentedPicker（延迟档位）、StatusDot、DarkScrollPanel、SettingsCard；PerMonitorV2 DPI 下按 uiScale 缩放布局。
- 网页自适应缩放：CalculateZoomFactor 按窗口宽度在 0.44~1.0 之间线性取值（726px 为 1.0）。

### 4.6 设置

- %LOCALAPPDATA%\MiniViewWebView2\settings.json，System.Web JavaScriptSerializer 序列化，350ms 防抖保存（settingsSaveTimer）。
- 项目包括：NormalBounds/LiveBounds、AlwaysOnTop、AutoLandscapeLive、ChromeHidden、三个自定义快捷键、Muted、AutoHideEnabled、AutoHideDelayMilliseconds、HideWhenInactive、HideLeftNav/HideTopBar/HideRightBar、ImmersiveMode 等；旧版本 settings.json 反序列化时自动用默认值补齐（self-test 覆盖升级场景）。
- 操作按钮：打开本地数据目录、恢复程序默认设置（不清登录）、清除登录和网页缓存（二次确认，不可撤销）。
- KeepOnScreen：保存的窗口位置若不在任何屏幕工作区内，则回退到主屏居中。

### 4.7 单 EXE 分发与单实例

- Bootstrap 入口将 5 个内置资源（主程序 EXE + config + WebView2 两个 DLL + WebView2Loader.dll）解压到 %LOCALAPPDATA%\MiniViewWebView2\App\1.6.0-x64；先比对字节一致则跳过，写入采用 temp+copy 原子替换；随后以该目录为工作目录启动真实程序，测试参数时等待退出并透传退出码。
- 单实例：Local\MiniViewWebView2.SingleInstance Mutex；重复启动时第二实例 Set 本地事件 Local\MiniViewWebView2.ShowWindow，首实例经 RegisterWaitForSingleObject 调用 ShowFromSecondInstance。
- 全局异常：ThreadException 与 AppDomain.UnhandledException 全部写入 Diagnostics 日志。

### 4.8 内置测试体系（无外部测试框架）

| 参数 | 内容 |
|---|---|
| --self-test | 17 项纯逻辑断言：抖音 URL 规则（含伪造域名）、直播 URL 识别、缩放系数、隐藏延迟归一化、设置默认值与旧设置升级、横屏尺寸/屏幕钳制、快捷键解析/冲突/保留键、圆角路径退化情形 |
| --smoke-test | 检查 WebView2 环境可初始化（加载本地测试页后退出） |
| --live-smoke-test | 加载 LiveSmokeTestHtml 模拟直播 DOM，验证横屏切换与栏位隐藏（失败退出码 3） |
| --settings-smoke-test | 加载设置面板，验证滚动、控件尺寸、多缩放布局一致性（失败退出码 4） |
| --dom-probe | 输出抖音页面 DOM 结构报告，用于网页改版后适配 |

## 5. 隐私边界（官方声明）

- 程序自身不收集、不上传用户信息；登录界面来自抖音官网。
- 隐藏功能只让窗口从桌面和任务栏消失，不能对系统管理员、任务管理器、远程管理、屏幕录制或进程审计隐藏。
- 抖音网页的数据处理受抖音隐私政策约束；WebView2 Runtime 可能按 Windows/Edge 系统策略发送诊断数据。

## 6. 小结

这是一个目标单一、工程约束很强的「抖音侧边小窗」工具：C# WinForms + WebView2 实现抖音网页容器，围绕「不打扰地看视频/直播」提供全局热键、多路自动隐藏、直播横屏纯画面、自定义快捷键与本地设置持久化；通过单 EXE 自解压分发和内置 self-test/smoke-test 在无 Visual Studio 环境完成构建与验证。代码总量约 3475 行，其中 76% 集中在 MainForm.cs（UI + 行为），逻辑规则（WindowRules/HotkeyDefinition）独立成模块并有自检验证。
