# 当前项目概览

## 范围

本次（2026-09-21）在 docs/mcp-optimization-implementation.md 所述优化落地后，重新静态阅读 README、使用说明、配置、scripts 与全部 src/ 源码结构。工作区路径 D:/Cline/douyin-mini-webview2。未启动应用、未构建、未运行测试，也未核验 GitHub 发布状态。没有修改业务代码；本文件为截至本次阅读的概览记录（更新自上一版概览）。

## 项目定位

波妞摸鱼是 Windows 抖音网页小窗工具，使用 C#、WinForms、.NET Framework 4.8 和 Microsoft Edge WebView2。并非独立视频平台或 Electron 应用；网页内容和播放依赖抖音与系统 WebView2 Runtime。当前源码版本为 1.8.0（src/AppVersion.cs 为版本唯一来源，scripts/check-version.ps1 构建前核对一致性），README 声明支持 Windows 10/11 的 x64、x86。

## 已有实现

- 无系统边框的小窗、置顶、缩放、拖动、普通和直播窗口位置尺寸记忆。
- 默认 Ctrl+Alt+D 隐藏/恢复窗口，Ctrl+Alt+B 隐藏/恢复工具栏，Ctrl+Alt+M 静音，Ctrl+Alt+F 清爽模式；窗口、工具栏、清爽模式快捷键支持设置。
- 鼠标移出自动隐藏，100/300/500 毫秒延迟；可选失焦隐藏；锁屏或会话断开时尝试隐藏。恢复快捷键不可用时部分隐藏逻辑会被阻止，避免无法恢复窗口。
隐藏时先隐藏原生窗口，再静音并派发带代次的暂停脚本（不等待网页脚本）；隐藏约 10 秒后尝试挂起 WebView，恢复窗口时唤醒并续播（详见 docs/mcp-optimization-implementation.md）。
- 清爽模式通过注入 CSS 隐藏页面装饰，保留原生播放器和切换布局；另有左导航、顶部、右互动栏独立开关。健康检测连续异常时清空注入样式，回退原网页布局。
- 通过 URL 和 DOM 探测直播，可选自动横屏，退出直播时恢复普通窗口布局。
- 导航限制为 HTTPS 抖音域名。登录与缓存使用本地 WebView2 用户目录，设置保存在本地 settings.json。
- 提供打开数据目录、恢复默认设置、清除登录与网页缓存入口。
- GitHub Releases 更新检查、按架构选择资产、SHA-256 校验、更新前备份设置和启动器、新版本健康检查失败后恢复旧启动器。
- 单实例控制、异常日志、UI 卡顿监测、自测和冒烟测试入口。
- PowerShell 构建和发布脚本，x64/x86 单 EXE 封装，可选代码签名、SHA-256 文件生成和 GitHub Release 上传。

## 模块划分

| 路径 | 职责 |
| --- | --- |
| src/Program.cs（216 行） | 入口、单实例、全局异常捕获、--self-test 与 --update-check-test |
| src/Bootstrap.cs | 单 EXE 资源释放到 %LOCALAPPDATA%\MiniViewWebView2\App\1.8.0-<架构>，轮换旧负载并启动内部程序 |
| src/MainForm.cs（656 行） | 主窗体骨架：工具栏、布局、圆角区域、缩放、设置保存、退出 |
| src/MainForm.Lifecycle.cs（227 行） | 隐藏/显示/挂起/会话事件与鼠标移出监测 |
| src/MainForm.Page.cs（201 行） | 导航守卫、直播探测与布局、清爽模式调度 |
| src/MainForm.Hotkeys.cs（187 行） | 快捷键录入、注册与冲突处理 |
| src/MainForm.Settings.cs（709 行） | 设置界面构建与布局、数据目录/重置/清缓存 |
| src/MainForm.WebView.cs（244 行） | WebView2 初始化、DOM 探针、调试截图 |
| src/MainForm.Updates.cs（82 行） | 更新检查入口 |
| src/MainForm.Recovery.cs（55 行） | 托盘恢复入口 |
| src/MainForm.Regression.cs（306 行） | --regression-test 场景与性能采样 |
| src/PageScripts.cs（216 行）/ src/MediaScripts.cs（37 行） | 页面显隐 CSS 注入 / 带代次的暂停恢复脚本 |
| src/Logic.cs（265 行） | WindowRules（URL 校验、屏幕约束、横屏计算）与 HotkeyDefinition |
| src/SettingsStore.cs（157 行） | AppSettings 与 JSON 原子持久化 |
| src/UpdateService.cs（349 行） | 更新发现、下载校验、安装脚本与回滚 |
| src/UpdateTests.cs（129 行） | 更新链路离线测试（HttpMessageHandler 桩） |
| src/UiControls.cs（624 行） | 自绘控件与深色调色板 |
| src/WindowLifecycle.cs / src/VisibilityAwareWebView.cs（各 15 行） | 操作代次防异步竞态 / WebView 可见性同步（挂起 0x8007139F 修复） |
| src/Diagnostics.cs（132 行） | 日志、128 项环形缓冲与 UI 卡顿监测 |
| src/FailureMessages.cs（28 行） | 更新与 WebView 失败的用户文案 |
| src/AppVersion.cs / src/AssemblyInfo.cs | 版本唯一来源 / 程序集元数据 |
| scripts/、assets/、artifacts/、docs/ | 构建发布测试脚本、图标、验证产物、分析与调整记录 |

## 注意事项

- 版本口径已统一为 1.8.0：src/AppVersion.cs 为唯一来源，app.manifest 为 1.8.0.0，使用说明与 README 由 scripts/check-version.ps1 在构建前核对。
- HideWindow 已改为先隐藏原生窗口、再后台静音并暂停（docs/mcp-optimization-implementation.md）；真实抖音页面上的阻塞与音频表现仍需人工验收。
- OnSessionSwitch（锁屏/会话断开）现为无条件隐藏；失焦和鼠标移出隐藏统一要求老板键或托盘至少有一个恢复入口（src/MainForm.Lifecycle.cs、src/MainForm.Recovery.cs）。
- 页面美化和直播适配依赖抖音 DOM，网页结构变化可能导致规则失效；已有回退措施不等于已验证所有情况。
- 主窗体已按职责拆分为 partial 文件，但共享状态仍集中在 MainForm；界面、网页注入与窗口状态的修改需跑 scripts/test.ps1 并针对性回归。
- 隐藏只是窗口级行为，不是对任务管理器、系统管理员或录屏审计隐身。

## 验证状态

本次为静态阅读补充：以 wc/grep 结构提取与定点读取为准。LSP 文档符号仍返回不确定的空结果（provider_state=unknown、semantic_result_inconclusive），不据此推断符号不存在。HideWindow 现为先隐藏后静音/暂停（src/MainForm.Lifecycle.cs 已核对）。未给出性能、安全或兼容性的完整审计结论。
