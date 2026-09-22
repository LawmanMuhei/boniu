# 剩余优化空间（全维度评估）

日期：2026-09-21。范围：在 docs/mcp-optimization-recommendations.md 六项建议（已实施，见 docs/mcp-optimization-implementation.md）之外，对当前源码与脚本做全维度静态复审后给出的下一轮候选。方法：定点读取 + 结构/坏味道扫描（search_files「async void」等），未构建、未运行、未做性能实测。标注「已核实」的条目有对应代码位置；其余为待验证候选，不是已确认缺陷。

## A. 性能

- [已核实] src/MainForm.Lifecycle.cs 的 ShowWindow 把 pointerTimer 置为 10ms（100Hz 轮询光标），且即使关闭自动隐藏也在跑。建议改 WM_MOUSELEAVE/TrackMouseEvent 事件驱动，保留隐藏态 250ms 心跳；或 AutoHideEnabled=false 时直接 Stop。
- [已核实] src/PageScripts.cs 清爽模式健康守卫 setInterval 1500ms 常驻，每轮 getBoundingClientRect 引发布局测量。建议改 MutationObserver，或应用样式后约 30 秒无异常即停表。
- 直播探测 liveTimer 周期 ExecuteScriptAsync 有 IPC 开销。可改为页面侧 hook（history.pushState/visibilitychange）后 postMessage 推送，C# 只收 WebMessage。
- 挂起延迟固定 10 秒（WebViewSuspendDelayMilliseconds）。可按隐藏来源分级（老板键即时挂起、移出隐藏 10 秒），已有空闲文档挂起回归可复用。

## B. 可靠性与正确性

- [已实施 2026-09-22] ApplyMediaState、ApplyPageElementVisibility、RunDomProbe 已改为 Task + 显式故障观察，不再保留业务 async void。
- [已实施 2026-09-22] SettingsStore.Load、会话事件、DumpWindowShot、MarkHealthy、二实例通知和设置保存等关键吞异常路径已补 Diagnostics 日志；诊断系统自身和预期字体回退仍保持不递归记录。
- [已实施 2026-09-22] 失焦隐藏和鼠标移出隐藏统一使用 CanRestoreWindow；托盘可恢复时允许隐藏，两种恢复入口都不可用时阻止隐藏。
- [已实施 2026-09-22] Bootstrap 按架构清理旧负载目录，每个架构保留当前版本和最近一个旧版本，删除失败不阻断启动。
- [已实施 2026-09-22] 更新前设置备份只保留最近三份；新版本健康启动后安装脚本清理 launcherPath.bak。
- [已实施 2026-09-22] 启动参数改用共享 CommandLineArguments 标准 Windows 引号算法，并补空参数、Unicode/空格路径、尾反斜杠和内嵌引号自测。

## C. 安全与供应链

- 代码签名缺失（已知）。SHA-256 sidecar 与 EXE 同信道发布，信道被攻破可同时替换。拿到证书前可考虑内嵌公钥（minisign/ed25519）独立验签；拿到后走 BONIU_SIGN_* 流程（已就绪）。
- [已核实] UpdateService.EnsureHttps 只校验 scheme。建议追加 host 白名单（github.com、objects.githubusercontent.com、*.githubusercontent.com），防止 API 响应被引向任意 HTTPS 主机。
- [已核实] build.ps1 构建期从 nuget.org 下载 WebView2 nupkg（已钉版本 1.0.4191.47）但不校验哈希。建议写入该 nupkg 的期望 SHA-256 并校验后解压，防供应链漂移。
- 子框架导航未设防：仅顶层 OnNavigationStarting 守卫。iframe 内非抖音内容风险低（沙箱 + 新窗口已拦），可选 FrameNavigationStarting 做防御性拦截，或在 docs 声明接受该风险。
- 已核实无需改动：IsDouyinUrl 伪造域识别、权限全拒、下载取消、限长下载（50MB/4KB hash）、哈希首字段解析、健康探针 token 校验。

## D. 架构与可维护性

- UpdateService（349 行）身兼发现/下载/校验/暂存/安装脚本/回滚六职，继续膨胀时再拆 Checker/Downloader/Installer。
- PageScripts 以 C# 字符串拼 CSS/JS，建议迁内嵌 .css/.js 资源，便于编辑与 lint。
- SelfTests/UpdateTests 内嵌 Program.cs；建议补 PowerShellLiteral、QuoteCommandArgument 边界用例（单引号、Unicode 路径、尾反斜杠）。
- Bootstrap 与 Program 重复 HasArgument/GetArgumentValue；两个编译单元各留一份可接受，可改共享源链接。
- WindowLifecycle 代次模式良好，可推广为统一「操作代次」服务（页面样式 revision、媒体命令已各自实现，可合并抽象）。

## E. 用户体验

- 自动隐藏与点击竞速：鼠标移出窗口去点右侧点赞/评论按钮会先隐藏。建议窗口外扩 N 像素触发缓冲区，或鼠标按住期间挂起自动隐藏。
- 托盘入口默认关闭，仅热键不可用时强制开启。建议默认开启，或首次自动隐藏时 toast 提示托盘位置。
- 静音热键 Ctrl+Alt+M 固定不可改，与其余三组可自定义不一致；可开放自定义（默认值不变，冲突检测已有）。
- 无障碍：自绘控件（UiControls）无 UIA 元数据，读屏不可用。至少为设置页关键控件补 AccessibleName/Role。
- 主题固定深色；可跟随系统浅色/高对比度。
- 功能候选：鼠标移到屏幕边缘的半透明「窥视」预览；老板键双击彻底退出；多账号配置档（多个 UserData 目录切换）；滚轮调音量/倍速；关闭按钮改为最小化到托盘选项。

## F. 测试与验证

- [已完成文档 2026-09-22] 真实抖音页人工验收步骤已固化为 docs/mcp-real-douyin-acceptance.md；真实账号、音频、锁屏、多显示器与实际更新仍需发布前人工执行。
- 回归全为合成页（已有性能限制声明）。发布前建议真实页 30 分钟长期内存/音频泄漏观察。
- 更新链路离线测试已覆盖下载/哈希/中断/超大/超时；缺安装脚本字面量转义的单测（见 D）。
- 建议 scripts/test.ps1 入口先跑 check-version.ps1（目前仅 build 间接调用）。

## G. 构建与发布

- publish.ps1 直接 gh release create，无 dry-run。建议加 -WhatIf 只打印将上传的资产与哈希。
- 签名时间戳仅 digicert http 单点；可加备用 TSA。
- 双名产物（波妞摸鱼.exe 与 BoniuMoyu.exe 拷贝）职责需在脚本注释中固化（内部名 vs 发布名）。

## H. 文档与社区

- 缺 CHANGELOG.md（可从 GitHub Releases notes 汇总）。
- 缺 Issue 模板：「页面适配失效」类反馈应附 --dom-probe 输出与截图，降低定位成本（这是本项目最易碎的依赖面）。
- 可选英文 README；隐私边界可补充 Cookie/LocalStorage 保存位置与清除方式细节。

## 建议落地顺序

1. E 的体验痛点（触发缓冲区、托盘默认开）+ B 的低成本可靠性（日志、CanRestoreWindow 统一、async void 包装）。
2. C 的供应链与更新加固（host 白名单、nupkg 哈希）+ B 的磁盘清理（旧负载目录、备份轮换）。
3. F 的真实页验收清单成文。
4. 其余按用户反馈与增长需要择机。

