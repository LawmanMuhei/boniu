# 波妞摸鱼 - 功能实现分析

**项目**: 波妞摸鱼 1.6.0 (MiniViewWebView2)  
**类型**: WebView2 原生 Windows 小窗  
**语言**: C# .NET Framework 4.8, 无 Electron/Chromium 打包  
**分析时间**: 2026-09-19  
**源码位置**: `src/` 7 文件 + `scripts/build.ps1`

---

## 一、总体架构

```
dist/波妞摸鱼.exe (Bootstrap 单文件)
  ↓ 首次运行解压到 %LOCALAPPDATA%\MiniViewWebView2\App\1.6.0-x64\
    - 波妞摸鱼.exe (主程序 payload)
    - Microsoft.Web.WebView2.Core.dll / WinForms.dll
    - WebView2Loader.dll
    - .config
  ↓ 启动主程序
Program.cs (单实例 + 异常 + 诊断)
  ↓
MainForm.cs (2650行核心)
  - WebView2 渲染 https://www.douyin.com/?recommend=1
  - 工具栏 + 设置面板 + 隐藏/置顶/静音/直播逻辑
  - UserData: %LOCALAPPDATA%\MiniViewWebView2\UserData (Cookie/LocalStorage)
  - Settings: %LOCALAPPDATA%\MiniViewWebView2\settings.json
  - Logs: %LOCALAPPDATA%\MiniViewWebView2\logs\run.log
```

**Bootstrap (`src/Bootstrap.cs`)**:
- `Version = "1.6.0"`，installRoot = `LocalAppData/MiniViewWebView2/App/1.6.0-x64`
- 内嵌资源 `payload.*` 5个文件，`ExtractResource` 读取 ManifestResourceStream，SHA256 比对，已存在且 hash 相同则跳过，否则写 `.new` 再原子 Copy
- `ProcessStartInfo` 启动真实 exe，透传 args，`--self-test/--smoke-test/--live-smoke-test/--settings-smoke-test` 时等待退出并返回 ExitCode
- 异常弹窗 "波妞摸鱼启动失败"

**Program (`src/Program.cs`)**:
- Mutex `Local\MiniViewWebView2.SingleInstance` 单实例，第二个实例通过 `EventWaitHandle Local\MiniViewWebView2.ShowWindow` 唤起首实例 `ShowFromSecondInstance()`
- `Diagnostics.Start(logs)`，注册 `ThreadException` + `UnhandledException`
- `MainForm` 4种测试模式：smoke / live-smoke / settings-smoke / dom-probe，使用临时 UserData `Temp/MiniViewWebView2Smoke/{pid}`

---

## 二、核心功能逐项实现

### 1. 安全导航与域名白名单 (`Logic.cs` - WindowRules)
- `IsDouyinUrl`: 仅 `https` + `douyin.com` 或 `*.douyin.com`，`Uri.TryCreate` + `DnsSafeHost` 校验
- `OnNavigationStarting`: 若 `!IsDouyinUrl(e.Uri)` 则 `e.Cancel = true`，阻断外链
- `OnNewWindowRequested`: 仅允许抖音域名的 `e.Uri` 才 `webView.Source = new Uri(e.Uri)`，否则取消
- `PermissionRequested` → `Deny`，`DownloadStarting` → `Cancel`
- WebView2 配置：`IsStatusBarEnabled=false`, `IsZoomControlEnabled=false`, `AreDevToolsEnabled=false`, `AreBrowserAcceleratorKeysEnabled=false`，`--autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling`

### 2. 隐藏/显示机制
- **全局热键** `Ctrl+Alt+D` (可自定义) → `ToggleWindow()`
  - `RegisterHotKey` (user32) 4个 ID: Window=1, Chrome=2, Mute=3, Immersive=4
  - `WndProc WM_HOTKEY` 触发
- **HideWindow()** (`MainForm.cs:664`):
  - 立即 `Hide()`，`ShowInTaskbar=false` 已设，无任务栏按钮
  - `outsideSince=null`，异步 `ExecuteScriptAsync(PauseVideosScript)` 暂停视频 + `CoreWebView2.IsMuted=true` (后台静音，不等网页响应)
  - 日志 `Diagnostics.Mark("HideWindow")`
- **ShowWindow()** (`689`):
  - `ShowGraceUntil = Now + 2200ms` 防抖，避免鼠标还在外部立即再隐藏
  - `Show()`，恢复 `IsMuted = settings.Muted`
  - `ResumeVideosScript`
- **鼠标离开自动隐藏** `pointerTimer` 10ms:
  - `MonitorPointer()` 检查 `Bounds.Contains(Cursor.Position)`，若在外部记录 `outsideSince`，若 `UtcNow - outsideSince >= AutoHideDelayMilliseconds` (100/300/500) 则 `HideWindow()`
  - 若在 `ShowGraceMilliseconds=2200` 内则忽略
  - 设置可关闭 `AutoHideEnabled`
- **失去焦点隐藏** `WM_ACTIVATEAPP wParam=0` → `HideWhenApplicationBecomesInactive()`，仅当 `HideWhenInactive=true` 且 `windowHotkeyAvailable`
- **系统级强制隐藏**: `Microsoft.Win32.SystemEvents.SessionSwitch` (锁屏/断开) + `SessionEnding` → `if(Visible && windowHotkeyAvailable) HideWindow()`

### 3. 工具栏与窗口行为
- **无边框窗体**: `FormBorderStyle=None`，`DoubleBuffered`，`BackColor=UiPalette.Shell` 深色
- **拖动**: toolbar `MouseDown` → `ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`
- **Resize**: `WM_NCHITTEST` 自定义 8方向 `HTLEFT/RIGHT/TOP/BOTTOM/...` grip=7px
- **圆角**: `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)`，若失败则 `Region` 圆角 `WindowCornerRadius=10`
- **置顶**: `TopMost = settings.AlwaysOnTop`，`pinButton` 切换，`ToggleAlwaysOnTop()`
- **工具栏按钮** (Segoe MDL2 Assets 图标):
  - 后退 `E72B` → `GoBack()`，刷新 `E72C` → `Reload()`
  - 置顶 `E718`，静音 `E767`，设置 `E713`，隐藏边框 `E8A7`，立即隐藏 `E890`，关闭 `E8BB` (hover 红色)
  - `LayoutToolbarButtons` 自适应宽度，`Scaled()` 按 DPI 缩放 `uiScale`
- **Bounds 记忆**: `NormalBounds` vs `LiveBounds` 分别保存，`KeepOnScreen` 保证在 `Screen.AllScreens.WorkingArea` 内，probe 点 `X+30,Y+30` 必须落在某 workArea，否则居中；`SetBoundsProgrammatically` + `changingBounds` 防递归

### 4. 快捷键系统 (`Logic.cs` HotkeyDefinition)
- 默认: Hide `Ctrl+Alt+D`, Chrome `Ctrl+Alt+B`, Mute `Ctrl+Alt+M` (硬编码), Immersive `Ctrl+Alt+F`
- 解析 `TryParse`: 至少1个修饰键 `Ctrl/Alt/Shift/Win` + 1个主键 `A-Z,0-9,F1-F24,Back,Delete,...`，单字母数字直接转 `Keys`，支持 `Space/Left/Right/Up/Down`
- 保留键拦截: `Alt+F4`, `Ctrl+Alt+Delete`, `Ctrl+Shift+Escape`, `Super+L` → `IsReservedByWindows()`
- 冲突检测: `ConflictsWith` 修饰键+主键完全相同，或与 Mute 硬编码冲突
- 设置面板录入: `CreateShortcutButton()` 点击后进入捕获模式，监听 `KeyDown` 非修饰键，验证后 `Serialize()` 存 `Control+Alt+D` 形式
- 注册失败 `windowHotkeyAvailable=false` 时自动禁用相关自动隐藏

### 5. 静音与媒体控制
- `core.IsMuted = settings.Muted` 初始，`ToggleMute()` 翻转 `Muted`，若隐藏状态则强制 `true`
- `PauseVideosScript` / `ResumeVideosScript`:
  ```js
  () => { document.querySelectorAll('video').forEach(v=>{try{v.pause()}catch{}}); }
  ```
  隐藏时先静音再暂停，不等待

### 6. 直播自动横屏与纯画面
- **检测**: `liveTimer` 250ms + `NavigationCompleted/SourceChanged` 触发 `DetectLiveStateAsync()`
  - `IsLiveUrl`: `live.douyin.com` 或 path `/live` / `/live/`
  - JS `LiveProbeScript`: `Boolean(document.querySelector('#PlayerLayout > .__livingPlayer__, [data-e2e="living-container"] #PlayerLayout'))` + URL 判断
  - `liveDetectionPending` 防并发
- **横屏切换**:
  - `UpdateLiveState(bool)`: `false->true` → `ApplyLiveLayout()`，`true->false` → `RestorePortraitBounds()`
  - `ApplyLiveLayout()`: 保存 `NormalBounds`，目标 `LiveBounds` 若无则 `CalculateLandscapeBounds(Bounds, workArea)` 800x450 或 16:9 计算，`KeepOnScreen(...,640,360)`
  - `liveLandscapeApplied` 标志，退出直播恢复普通窗口
  - 位置大小分别持久化 `settings.LiveBounds` / `NormalBounds`
- **纯画面隐藏** `BuildPageVisibilityScript()` 动态生成 CSS + JS:
  - 始终隐藏左右箭头 `div[aria-label="上一条/下一条"]`, `switch-btn`, `arrow-left/right`, `slideArrow`
  - 可选 `HideLeftNav`: `[data-e2e="douyin-navigation"]`, `SideBar`, `leftSidebar`
  - 可选 `HideTopBar`: `searchbar`, `Header`, `TopBar`, `SearchBar`, `top-banner`
  - 可选 `HideRightBar`: `video-sidebar`, `SideToolbar`, `ActionBar`, `InteractBar`
  - **沉浸模式** `ImmersiveMode` (id级特异性 `#dark` 压过抖音 CSS-in-JS):
    - 隐藏 `#douyin-header`, `#douyin-navigation`, `searchbar-input/button`, `im-entry`, `something-button`, `live-avatar`, `video-avatar`, `digg`, `comment-icon`, `collect`, `share`, `play-more`, `danmaku`, `video-info`
    - 固定 `#douyin-right-container` 为 `position:fixed;left:0;top:0;width:100vw;height:100vh;background:#000;z-index:100`
    - 撑满 `.parent-route-container`, `#slidelist`, `feed-item/video/live`, `#sliderVideo`, `slider-card` 全 `100vw/100vh`
    - 视频 `width:100%;height:100%`
    - 改完触发 `resize` 事件 2次 (0ms + 320ms) 让播放器重算高度
  - 注入到 `id=__boniuPageStyle` 的 `<style>`，每次设置变更重建

### 7. 沉浸模式工具栏显隐
- `ToolbarCollapsed`: `ChromeHidden || (ImmersiveMode && !toolbarRevealed)`
- `UpdateImmersiveToolbar()`: 鼠标 `PointToClient(Cursor)` Y 在 `ToolbarRevealHeight=44` 内则 `toolbarRevealed=true`，否则 false，`LayoutWindow()` 重排
- 快捷键 `Ctrl+Alt+F` → `ToggleImmersiveMode()`

### 8. 设置系统
- **存储**: `SettingsStore` 用 `JavaScriptSerializer` JSON，UTF8 无 BOM，路径 `settings.json`
- **字段** (`AppSettings`):
  - `NormalBounds`, `LiveBounds` (X,Y,Width,Height)
  - `AlwaysOnTop` (默认 true)
  - `AutoLandscapeLive` (直播横屏)
  - `ChromeHidden` (边框隐藏)
  - `HideShortcut` (默认 Ctrl+Alt+D), `ChromeShortcut` (Ctrl+Alt+B), `ImmersiveShortcut` (Ctrl+Alt+F)
  - `Muted`, `AutoHideEnabled` (默认 true), `AutoHideDelayMilliseconds` 100/300/500 (Normalize)
  - `HideWhenInactive`, `HideLeftNav`, `HideTopBar`, `HideRightBar`, `ImmersiveMode`
- **UI**: `DarkScrollPanel` 自定义滚动，`SettingsCard` 圆角卡片，`ToggleSwitch` 自绘开关，`SegmentedPicker` 3段选择延迟，`StatusDot` 绿/黄/红，`RoundedButton`
- **保存**: `settingsSaveTimer` 400ms debounce，`SaveSettingsNow()` 写 `AlwaysOnTop`, 快捷键序列化
- **操作**:
  - 打开本地数据目录 → `Process.Start(appFolder)` (`MiniViewWebView2`)
  - 恢复默认设置 → `new AppSettings()` 保留 Bounds，重置其他
  - 清除登录缓存 → 二次确认 `MessageBox`，删 `UserData` 文件夹 + `settings.json`，需重登

### 9. 诊断与日志 (`Diagnostics.cs`)
- Ring buffer 96 条 `Ring[]` + `RingTicks[]`，`Mark(step)` + `Tick()` 在热路径无磁盘 IO
- `Start(folder)`: 创建 `logs/run.log`，>512KB 删除，`watchdog Timer 2s` 检测 `uiTicks` 是否停滞，若停滞 `stallPeriods++` 写日志 dump ring
- `Log(message)` 加锁 `Gate` `AppendAllText` 带时间戳+线程ID
- `LogException(context, ex)` 记堆栈

### 10. 构建 (`scripts/build.ps1`)
- 要求 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 下载 NuGet `Microsoft.Web.WebView2 1.0.4191.47` 从 `api.nuget.org/v3-flatcontainer`，解压到 `.packages`
- 编译 payload: `csc /target:winexe /platform:x64 /optimize+ /win32icon:assets/boniu-moyu.ico /win32manifest:app.manifest /out:build/payload/波妞摸鱼.exe` + 7个 src + 引用 `System, System.Core, System.Drawing, System.Web.Extensions, System.Windows.Forms, WebView2.Core/WinForms, System.Runtime Facade`
- 编译 test exe: 同源码 `/target:exe` 用于 `--self-test`
- 拷贝 `app.config` → `.exe.config`，`WebView2Loader.dll` + 2个 WebView2 dll 到 payload/build
- 编译 bootstrap: `/target:winexe` + `/resource:payload...` 5个资源 + `AssemblyInfo.cs + Bootstrap.cs` → `dist/波妞摸鱼.exe` 单文件交付

---

## 三、功能清单对照 README

| README 功能 | 实现位置 |
|---|---|
| 只允许 HTTPS 抖音域名 | `Logic.IsDouyinUrl` + `NavigationStarting/NewWindowRequested` |
| Cookie 保存在 UserData | `CoreWebView2Environment.CreateAsync(null, userDataFolder)` |
| Ctrl+Alt+D 隐藏/显示 | `HotkeyWindow` + `ToggleWindow` + `HideWindow/ShowWindow` |
| 隐藏后无任务栏 | `ShowInTaskbar=false` + `Hide()` |
| 鼠标移回不恢复 | 仅热键恢复，`outsideSince` 仅触发隐藏 |
| 鼠标离开延迟隐藏+静音暂停 | `pointerTimer 10ms` + `PauseVideosScript` + `IsMuted=true` |
| 可关闭自动隐藏，0.1/0.3/0.5s | `AutoHideEnabled` + `SegmentedPicker` + `NormalizeAutoHideDelay` |
| 切换程序自动隐藏 | `WM_ACTIVATEAPP` + `HideWhenInactive` |
| 锁屏/断开会话强制隐藏 | `SystemEvents.SessionSwitch` |
| Ctrl+Alt+B 隐藏工具栏 | `HotkeyChrome` + `ApplyChromeState` |
| Ctrl+Alt+M 静音 | `muteHotkey` 硬编码 + `ToggleMute` |
| 自定义快捷键 | `HotkeyDefinition.TryParse` + 捕获 UI |
| 直播自动横屏 | `liveTimer` + `LiveProbeScript` + `ApplyLiveLayout` |
| 直播隐藏主播栏/礼物栏/聊天区 | `BuildPageVisibilityScript` immersive CSS |
| 分别保存普通/直播窗口位置 | `NormalBounds` / `LiveBounds` + `KeepOnScreen` |
| 打开数据目录/恢复默认/清除缓存 | `openDataButton/resetSettingsButton/clearDataButton` |
| 单 EXE 解压到 LocalAppData | `Bootstrap.ExtractResource` + SHA256 |
| 隐私边界说明 | README + 仅隐藏窗口，不防任务管理器 |

---

## 四、安全与隐私

- 仅 `https://*.douyin.com`，其他协议/域名直接 Cancel
- 登录页来自抖音官网，程序不读取手机号/密码/Cookie，不上传
- WebView2 数据本地化，无额外网络请求
- 隐藏仅 UI 层面，`README` 明确不能对抗管理员/任务管理器/录屏/审计

---

## 五、测试模式

- `--self-test`: `SelfTests.Run()` (在 Logic.cs)
- `--smoke-test`: 渲染 `波妞摸鱼 WebView2 OK` 1.2s 退出码 0
- `--live-smoke-test`: 渲染假直播 HTML，检查 `liveLandscapeApplied && overlaysHidden` 退出码 0/3
- `--settings-smoke-test`: 渲染设置面板，检查 `CanScroll`, `ScrollOffsetY`, `SelectedIndex`, `Bottom <= ScrollContentHeight`, `LayoutSurvivesScrollAndRelayout/Resize/Scales`, `dwmRoundsCorners == (Region==null)` 退出码 0/4
- `--dom-probe`: 9s 后执行 `DomProbeScript` 输出 DOM 结构

---

## 六、未实现/可改进点

- 无自动更新，版本硬编码 1.6.0
- 快捷键仅支持 `A-Z,0-9,F1-F24` 等，未支持 `Oem` 键
- 直播检测依赖抖音 DOM `data-e2e`，抖音改版需更新 `BuildPageVisibilityScript` 选择器
- `JavaScriptSerializer` 已过时，可迁移 `System.Text.Json`
- 无最小化到托盘，仅隐藏

---

**结论**: 项目是精简的 C# WinForms + WebView2 封装，核心价值在隐藏逻辑、直播横屏、页面净化三块，代码 2650行主窗体已覆盖所有 README 功能，Bootstrap 实现单文件交付，诊断环形缓冲便于排查卡顿。
