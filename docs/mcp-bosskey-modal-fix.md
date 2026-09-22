# 老板键弹窗泄露修复（P0 实施记录）

日期：2026-09-21。范围：docs/mcp-ui-review.md 中的 P0 问题「老板键 × 模态对话框泄露」。全部修改在本地工作区完成，未提交、未发布、未签名；dist/ 未动。

## 问题

主窗隐藏（老板键/自动隐藏/失焦）只隐藏 Form 本身；Win32 下 owner 隐藏不会连带隐藏 owned 弹窗，而 WM_HOTKEY 在模态消息循环中仍派发到主窗 WndProc——弹窗开着时按老板键，主窗消失、对话框（标题常带「波妞摸鱼」）留在屏幕上，等于一键自曝。全量盘点共 12 处活跃模态调用（含启动时热键占用三连警告框、更新确认、危险操作确认等）。

## 修改

1. 清场安全网（根治层）：新增 DismissOwnedPopups（src/MainForm.Lifecycle.cs），HideWindow 入口调用。枚举 OwnedForms（Close）与本线程顶层 owned 可见窗（EndDialog 按「取消=2」结束，失败回退 PostMessage(WM_CLOSE)），覆盖现有与未来全部弹窗；确认类操作被清场即等价放弃，语义安全。NativeMethods 增补 EnumThreadWindows/GetCurrentThreadId/GetWindow/IsWindowVisible/PostMessage/EndDialog（src/SettingsStore.cs）。
2. 更新确认去模态（最高频弹窗）：删除 ShowUpdateConfirmation；改两步式——检查到新版后状态行显示「发现 vX · 更新说明首行」，按钮变为「下载并安装 vX」，再次点击才下载安装（原弹窗文案本就写着「点击检查更新以安装」，现语义一致）。启动自动检查永不打断窗口；下载失败按钮变「重试安装 vX」。新增 DownloadAndInstallAsync（src/MainForm.Updates.cs）。
3. 提示类弹窗全部行内化：ShowOwnedMessage 改为 pageNotice 行内横幅（ShowTransientNotice，7 秒自动消失，tooltip 保留全文，点击提前关闭）；覆盖：已是最新版、更新错误、打开数据目录失败、清缓存未就绪与失败、隐藏前快捷键警告。启动时热键占用三连警告框合并为一条横幅（src/MainForm.Hotkeys.cs）。无边框模式警告改横幅（src/MainForm.cs）。
4. 保留模态（安全网兜底）：恢复默认设置/清除登录缓存的 Yes/No 确认（需要明确的默认「否」；清场=取消，安全）；WebView 启动失败框（多行说明必要，重新打开可复现）；Bootstrap 启动失败框（主窗尚不存在，不适用本问题）。

## 测试

- scripts/test.ps1 -Platform x64 与 -Platform x86 全绿：Version consistency OK 1.7.0；自测 63 项通过；smoke / live-smoke / settings-smoke 通过；回归 53 项通过（含 20 次快速显隐，覆盖 DismissOwnedPopups 路径）。报告：artifacts/verification/tests-x64.json、tests-x86.json。
- 未自动化：真实老板键 × 弹窗并存的目测验收（见下）。

## 人工验收清单

1. 触发任一横幅（如手动检查更新）→ 按 Ctrl+Alt+D → 横幅与主窗同时消失，屏幕无残留。
2. 打开「清除登录和网页缓存」确认框 → 按 Ctrl+Alt+D → 确认框消失且数据未被清除；恢复窗口后可重新操作。
3. 若能模拟热键冲突启动 → 警告以横幅出现（不弹框）且 7 秒自动消失。
4. 发现新版时：设置页按钮显示「下载并安装 vX」；第一次点击不下载，第二次点击才开始；全程无弹窗。
5. WebView 失败场景：弹框仍在；此时按老板键弹框应一并消失（安全网生效）。

## 未改动/边界

- dist/ 现有 EXE 为旧构建；本轮改动位于源码，发布需按 scripts/publish.ps1 流程另行执行。
- 行内横幅文本过长时省略显示（tooltip 全文）。其余 UI 优化候选见 docs/mcp-ui-review.md 与 docs/mcp-optimization-remaining.md。

