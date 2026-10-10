# 当前项目概览

## 范围

本次（2026-10-10）在完成清爽模式评论面板误回退修复、顶部工具栏迟滞与浮层改造、更新供应链加固后，实际跑通 x64/x86 自动测试并把版本提升到 1.9.0，**已发布到 GitHub**：源码推送到 LawmanMuhei/boniu 的 main（提交 1df64d5 与 43fd885），Release `v1.9.0` 为 latest，含 8 个资产。工作区路径 D:\muhei\boniu-main。本轮实际执行了 `scripts\check-version.ps1`、`scripts\test.ps1 -Platform x64|x86` 与 `scripts\publish.ps1 -Version 1.9.0 -WhatIf`；未签名、未安装更新，未做真实抖音页面人工验收。本文件为截至本次的概览记录（更新自上一版概览）。

## 项目定位

波妞摸鱼是 Windows 抖音网页小窗工具，使用 C#、WinForms、.NET Framework 4.8 和 Microsoft Edge WebView2。并非独立视频平台或 Electron 应用；网页内容和播放依赖抖音与系统 WebView2 Runtime。当前源码版本为 1.9.0（src/AppVersion.cs 为版本唯一来源，scripts/check-version.ps1 构建前核对一致性），README 声明支持 Windows 10/11 的 x64、x86。

## 已有实现

- 无系统边框的小窗、置顶、缩放、拖动、普通和直播窗口位置尺寸记忆。
- 默认 Ctrl+Alt+D 隐藏/恢复窗口，Ctrl+Alt+B 隐藏/恢复工具栏，Ctrl+Alt+M 静音，Ctrl+Alt+F 清爽模式，Ctrl+Alt+S 迷你小窗；窗口、工具栏、清爽模式快捷键支持设置。
- 清爽模式下工具栏收起时，光标贴到窗口顶部 6 像素以内（或上甩越过上边缘）才临时唤出，离开工具栏区域 44 像素后收起；进出阈值不同形成迟滞，避免一弹出就被推下去、指针下移一点立刻收起造成抖动，同时保证能点到按钮。唤出的工具栏以**浮层**压在画面之上（不占布局高度），因此播放区在收起/唤出两种状态下尺寸完全一致，视频不动、网页也不会收到 resize 而重排（见 docs/mcp-toolbar-reveal-hysteresis.md）。
- 鼠标移出自动隐藏，100/300/500 毫秒延迟；可选失焦隐藏；锁屏或会话断开时尝试隐藏。鼠标按下或拖动期间暂停自动隐藏，避免移出点击互动区时误隐藏。
- 隐藏时先隐藏原生窗口，再静音并派发带代次的暂停脚本（不等待网页脚本）；隐藏约 10 秒后尝试挂起 WebView，恢复窗口时唤醒并续播（详见 docs/mcp-optimization-implementation.md）。
- 清爽模式通过注入 CSS 隐藏页面装饰，保留原生播放器和切换布局；另有左导航、顶部、右互动栏独立开关。健康检测连续异常时清空注入样式，回退原网页布局。健康判定按「可见画面占视口的比例」，并在抖音评论面板展开时跳过判定，避免把页面自身重排误判成布局损坏（见 docs/mcp-immersive-comment-panel-fallback.md）。
- 通过 URL 和 DOM 探测直播，可选自动横屏，退出直播时恢复普通窗口布局。
- 导航限制为 HTTPS 抖音域名。登录与缓存使用本地 WebView2 用户目录，设置保存在本地 settings.json。
- 提供打开数据目录、恢复默认设置、清除登录与网页缓存入口。
- GitHub Releases 更新检查、按架构选择资产、SHA-256 校验、更新前备份设置和启动器、新版本健康检查失败后恢复旧启动器。
- 更新下载地址只接受 github.com 与 *.githubusercontent.com，防止 Release API 响应把下载引向任意 HTTPS 主机。
- 单实例控制、异常日志、UI 卡顿监测、自测和冒烟测试入口。
- PowerShell 构建和发布脚本，x64/x86 单 EXE 封装，可选代码签名、SHA-256 文件生成和 GitHub Release 上传；发布脚本支持 -WhatIf 只打印不上传。

## 模块划分

| 路径 | 职责 |
| --- | --- |
| src/Program.cs（237 行） | 入口、单实例、全局异常捕获、--self-test 与 --update-check-test |
| src/Bootstrap.cs（132 行） | 单 EXE 资源释放到 %LOCALAPPDATA%\MiniViewWebView2\App\1.9.0-<架构>，按架构轮换旧负载并启动内部程序 |
| src/MainForm.cs（691 行） | 主窗体骨架：工具栏、布局、圆角区域、缩放、设置保存、退出 |
| src/MainForm.Lifecycle.cs（316 行） | 隐藏/显示/挂起/会话事件与鼠标移出监测 |
| src/MainForm.Page.cs（225 行） | 导航守卫、直播探测与布局、清爽模式调度与健康守卫回退处理、顶部工具栏唤出与浮层 |
| src/MainForm.Hotkeys.cs（224 行） | 快捷键录入、注册与冲突处理 |
| src/MainForm.Settings.cs（672 行） | 设置界面构建与布局、数据目录/重置/清缓存 |
| src/MainForm.WebView.cs（242 行） | WebView2 初始化、DOM 探针、调试截图 |
| src/MainForm.Updates.cs（84 行） | 更新检查入口 |
| src/MainForm.Recovery.cs（51 行） | 托盘恢复入口与 CanRestoreWindow 判定 |
| src/MainForm.Regression.cs（298 行） | --regression-test 场景与性能采样（含评论面板不误回退用例） |
| src/PageScripts.cs（260 行）/ src/MediaScripts.cs（36 行） | 页面显隐 CSS 注入与清爽模式健康守卫 / 带代次的暂停恢复脚本 |
| src/Logic.cs（276 行） | WindowRules（URL 校验、屏幕约束、横屏计算、顶部唤出迟滞与浮层判定）与 HotkeyDefinition |
| src/SettingsStore.cs（164 行） | AppSettings 与 JSON 原子持久化 |
| src/UpdateService.cs（352 行） | 更新发现、下载校验（主机白名单）、安装脚本与回滚 |
| src/UpdateTests.cs（149 行） | 更新链路离线测试（HttpMessageHandler 桩，含主机白名单用例） |
| src/UiControls.cs（581 行） | 自绘控件与深色调色板 |
| src/WindowLifecycle.cs（15 行）/ src/VisibilityAwareWebView.cs（14 行） | 操作代次防异步竞态 / WebView 可见性同步（挂起 0x8007139F 修复） |
| src/Diagnostics.cs（119 行） | 日志、128 项环形缓冲与 UI 卡顿监测 |
| src/CommandLineArguments.cs（51 行） | 标准 Windows 命令行引号算法（附自测） |
| src/FailureMessages.cs（26 行） | 更新与 WebView 失败的用户文案 |
| src/AppVersion.cs（9 行）/ src/AssemblyInfo.cs（7 行） | 版本唯一来源 / 程序集元数据 |
| scripts/（build 174 / test 61 / publish 97 / check-version 22 行） | 构建、验证测试、发布与版本一致性核对 |
| assets/、artifacts/、docs/ | 图标、验证产物（tests/regression JSON 与设置页截图）、分析与调整记录 |

源码合计 5231 行（src/ 下 25 个 .cs）。

## 注意事项

- 版本口径已统一为 1.9.0：src/AppVersion.cs 为唯一来源，app.manifest 为 1.9.0.0，使用说明与 README 由 scripts/check-version.ps1 在构建前核对。
- v1.9.0 已发布：源码在 LawmanMuhei/boniu 的 main（1df64d5 功能提交 + 43fd885 版本提交），Release v1.9.0 为 latest、含 8 个资产，其中 4 个自动更新资产带显示标签。发布用 REST API 完成（本机未装 gh）：先建草稿、传完资产再公开，中途失败只会留下草稿。
- 本地工作区**不是 git 仓库**（源码自压缩包解压）；推送与发布是在工作区内临时克隆的副本里完成的，完成后已删除。下次发布同样需要先克隆或重新初始化仓库。
- HideWindow 为先隐藏原生窗口、再后台静音并暂停（docs/mcp-optimization-implementation.md）；真实抖音页面上的阻塞与音频表现仍需人工验收。
- OnSessionSwitch（锁屏/会话断开）为无条件隐藏；失焦和鼠标移出隐藏统一要求 CanRestoreWindow（窗口快捷键或托盘至少一个可用）。
- 构建期不再只信任版本号：scripts/build.ps1 在解压前校验 WebView2 nupkg 的 SHA-256（当前固定 1.0.4191.47，哈希随版本号一起更新）。
- 更新下载只接受 GitHub 主机；该检查约束的是 Release API 响应给出的初始地址，GitHub 自身发出的重定向仍由系统 HTTP 栈跟随（详见 docs/mcp-update-supply-chain-hardening.md）。
- scripts/publish.ps1 -WhatIf 仍会真实构建并生成 dist 资产（校验文件里的哈希必须来自真实文件），只跳过上传与显示标签。
- 页面美化和直播适配依赖抖音 DOM，网页结构变化可能导致规则失效；已有回退措施不等于已验证所有情况。
- 主窗体已按职责拆分为 partial 文件，但共享状态仍集中在 MainForm；界面、网页注入与窗口状态的修改需跑 scripts/test.ps1 并针对性回归。
- 隐藏只是窗口级行为，不是对任务管理器、系统管理员或录屏审计隐身。

## 验证状态

2026-10-10 在本工作区实际跑通自动化验证，结果为**当日源码状态下的通过记录**，不是性能或兼容性审计结论：

- `scripts\check-version.ps1`：Version consistency OK: 1.9.0。
- `scripts\test.ps1 -Platform x64` 与 `-Platform x86`：各自 5 个用例（--self-test、--smoke-test、--live-smoke-test、--settings-smoke-test、--regression-test）全部 exit 0，无超时；自测 **91 项通过**，回归 **55/55 检查通过**（含新增的 `narrowed player does not trigger fallback`、`comment panel does not trigger fallback`，同时保留画面被压坏时仍触发回退的用例）。报告为 artifacts/verification/tests-x64.json、tests-x86.json、regression-x64.json、regression-x86.json 与 settings-x64.png、settings-x86.png。
- **完整测试套件需要在非受限模式下运行**：受控沙箱允许在 `%TEMP%` 新建文件，但拒绝设置自测里的原子替换（`File.Replace`），会报 `SettingsStore.Save` 的 `UnauthorizedAccessException` 假失败。`-OutputRoot` 指向工作区根下新建目录可绕开 `artifacts`/`build`/`dist` 的写入限制，但绕不开这一条。
- `scripts\publish.ps1 -Version 1.9.0 -WhatIf`：exit 0，未上传；已核对 dist 中波妞摸鱼.exe / BoniuMoyu.exe / BoniuMoyu-1.9.0-x64.exe 三者哈希一致（x64 sha256 14118ac6…，x86 51e90c17…），4 个 .sha256 与对应 EXE 全部匹配。
- Release v1.9.0 已由 GitHub API 独立复核：tag 指向 main、draft=false、prerelease=false、为 latest，8 个资产大小与 dist 一致，4 个标签与中文发布说明均无乱码。
- 回归性能采样为隔离的合成 WebView2 夹具，**不代表真实抖音解码、网络和长期内存表现**，也不是修改前后的性能提升证明。
- 真实页面、真实音频、多显示器迁移、系统锁屏、托盘交互及实际更新安装仍需按 docs/mcp-real-douyin-acceptance.md 人工验收；本轮未执行。**清爽模式评论面板误回退的修复同样只在合成夹具上验证过**，真实评论区交互需人工确认。
- 尚无代码签名证书：SHA-256 与 GitHub HTTPS 可校验下载完整性，Windows 仍可能显示"未知发布者"。
