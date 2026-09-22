# 波妞摸鱼优化实施记录

日期：2026-09-21。范围：`docs/mcp-optimization-recommendations.md` 中的六项建议。全部修改均在本地工作区完成，未提交、未发布、未安装更新，也未替换 `dist/` 中现有的发布 EXE。

## 1. 隐藏、恢复与异步状态

- `HideWindow` 先隐藏原生窗口，再设置 WebView 静音并派发暂停脚本，不再在隐藏路径上等待网页脚本。`Hide()` 到返回的耗时写入 `run.log`（`hide native-ms`）。
- 新增 `src/WindowLifecycle.cs`：UI 线程持有的操作代次。隐藏、显示、导航或媒体刷新都会使旧代次过期，`SuspendWebViewIfHiddenAsync`、`DetectLiveStateAsync` 等异步完成时检查代次，不再修改更新后的窗口状态。
- 新增 `src/MediaScripts.cs`：暂停/恢复命令携带代次；页面侧拒绝过期命令，恢复后若窗口已再次隐藏则立即回暂停。原先“隐藏时曾播放”的集合只记录当时正在播放的视频，用户手动暂停的视频不会被自动播放。
- 挂起：`TrySuspendAsync` 期间发生显示或导航时丢弃结果并恢复；失败或被运行时拒绝时按 5 秒重试；`Resume` 成功才清除挂起标记。
- 发现并修复：隐藏窗体后 WebView2 控制器仍处于可见状态，`TrySuspendAsync` 抛出 `0x8007139F`（ERROR_INVALID_STATE），挂起从未成功。新增 `src/VisibilityAwareWebView.cs`，在隐藏、显示和挂起前重新执行 SDK 的可见性同步。修复后 x64/x86 回归均记录 `suspended: true`。原始二进制未复测，但原实现使用同一隐藏后挂起模式。
- `webView.CoreWebView2.IsMuted` 初始化时以窗口可见性为准；关机或任务管理器关闭时保存设置并退出，而不是取消关闭。
- `Diagnostics` 环形缓冲改为 128 项。原 96 项与位掩码索引不匹配，浪费三分之一槽位，并非崩溃缺陷。

## 2. 页面适配与回退

- 注入脚本迁移到 `src/PageScripts.cs`，`MainForm.Page.cs` 只负责状态与调度。清爽模式 CSS 规则本身未改动。
- 样式注入携带修订号；回退消息带修订号，来源限定抖音域名（测试模式除外），旧页面消息不会影响新页面状态。
- 直播适配从内联 `style` 改为可移除的 `<style id="__boniuLiveStyle">`，退出直播后页面恢复原有布局。
- 回退提示：布局健康检查回退或导航失败时，在视频区域顶部显示可点击提示；设置页“重新应用页面样式”可手动重试；设置状态文字同步显示回退。

## 3. 主窗体职责拆分

`src/MainForm.cs` 从 2897 行降至约 660 行，其余按职责拆为 partial 文件：`MainForm.Lifecycle.cs`（隐藏/显示/挂起/会话）、`MainForm.Page.cs`、`MainForm.Settings.cs`、`MainForm.Hotkeys.cs`、`MainForm.Updates.cs`、`MainForm.WebView.cs`、`MainForm.Recovery.cs`、`MainForm.Regression.cs`；自绘控件与调色板移到 `src/UiControls.cs`。拆分按原方法边界进行，未重写实现。

## 4. 回归测试与验证发现的问题

- `scripts/test.ps1`：隔离构建到 `artifacts/verification/`，依次运行自测、WebView 冒烟、直播布局、设置缩放和回归场景，输出 `tests-<架构>.json`、`regression-<架构>.json` 和设置页截图。超时只终止本次启动的测试进程。
- `--regression-test`：使用合成 `captureStream` 视频的本地页面，覆盖 20 次快速隐藏/恢复、渲染器繁忙时隐藏、挂起中恢复、过期媒体命令、清爽模式与设置往返、布局回退与手动重试、直播横屏进入/退出、导航失败提示、空闲文档挂起，以及恢复后屏幕像素采样确认 WebView 实际在渲染。
- `src/UpdateTests.cs`：离线 `HttpMessageHandler` 覆盖下载成功、哈希不匹配、正文中断、超大文件、断网、超时、Release 资产选择、非 HTTPS 拒绝，以及设置原子写入和损坏回退。
- 自测由 33 项增至 63 项，回归场景 53 项检查，x64 与 x86 均通过。
- 测试发现并修复的既有问题：窄窗口下设置页标签保留了默认 23px 高度，越出卡片边界（基线版本 `--settings-smoke-test` 同样失败，退出码 4）。`FitLabel` 改为按单行文本测量尺寸。
- 测试模式使用临时数据目录、不注册全局快捷键、不创建单实例事件、不写真实设置。

## 5. 性能测量

`regression-<架构>.json` 的 `performance` 记录合成页面在播放、隐藏、挂起、恢复及多轮操作后的 CPU 与内存。本机 x64 一次样本：播放约 2.1% 整机 CPU，隐藏后约 0.55%，挂起后约 0.36%；隐藏派发最长约 7.5 ms，恢复播放约 18 ms。

限制：合成页面不代表真实抖音的解码、网络和长期内存；工作集包含共享页重复计数；单次样本不构成统计结论，也不是修改前后的对比证明。真实页面基线仍需人工采集。

## 6. 版本、说明与失败提示

- 新增 `src/AppVersion.cs` 作为版本唯一来源；`AssemblyInfo.cs`、`Bootstrap.cs` 引用它，构建时改写清单版本。`scripts/check-version.ps1` 在构建和发布前核对清单、README 和使用说明。
- `app.manifest` 从 1.3.2.0 更新到 1.7.0.0；使用说明重写为 1.7.0，补充 x86、托盘、回退提示、更新失败和隐私边界。
- 托盘恢复入口（`MainForm.Recovery.cs`）：默认关闭，可在设置开启；恢复快捷键被占用时自动保留，提供显示、隐藏、恢复工具栏并打开设置、退出。
- 更新：下载增加 3 分钟传输上限与取消令牌；`.part` 文件失败即清理；哈希文件只接受首个 64 位十六进制字段；`UpdateService` 实现 `IDisposable`；窗口隐藏时发现更新不弹确认框；错误提示按异常类型给出可操作建议（`src/FailureMessages.cs`）。
- 设置改为临时文件加 `File.Replace` 原子写入。
- 签名：`build.ps1` 签名后验证 Authenticode 状态；`-RequireSigning` 在缺少证书时直接失败；`publish.ps1` 透传该开关。未申请或配置任何证书。

## 未完成或需人工验收

- 真实抖音页面上的清爽模式、直播切换、音频泄漏、多显示器与 DPI 迁移、锁屏隐藏、托盘交互、实际更新安装与回滚。
- 原始二进制的挂起失败未单独复现；原始代码的设置页越界已通过基线测试确认。
- 未在 GitHub 发布新版本；`dist/` 内文件为修改前构建。候选构建位于 `artifacts/candidate/dist/`，未签名。
- 建议发布前执行：`scripts/check-version.ps1`、`scripts/test.ps1 -Platform x64`、`scripts/test.ps1 -Platform x86`，并按上述清单人工验收。
