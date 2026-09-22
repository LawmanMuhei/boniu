# 第一批可靠性与体验优化实施记录

日期：2026-09-22。范围：docs/mcp-next-optimization-roadmap.md 的第一批建议。全部修改位于当前未提交工作区；未提交、未推送、未发布，代码签名仍因无证书而跳过。

## 已实施

1. 自动隐藏防误触：窗口四周缓冲由 7 像素扩大为按 UI 缩放的 18 设计像素；任意鼠标键按住期间清空离开计时，释放后重新按配置延迟计算。
2. 恢复能力统一：鼠标移出隐藏与失焦隐藏都使用 CanRestoreWindow，老板键或可见托盘任一可用即可安全隐藏。
3. 异步与日志：三个业务 async void 改为 Task + ObserveTask；页面检测、设置读取/保存、会话订阅、截图、健康标记和二实例通知失败写入 Diagnostics。
4. 参数引用：新增 src/CommandLineArguments.cs，实现符合 CommandLineToArgvW 规则的反斜杠/引号编码，应用与 Bootstrap 共用；构建脚本显式纳入 Bootstrap 编译。
5. 磁盘轮换：每个架构的负载目录保留当前版本和最近一个旧版本；更新前设置备份保留最近三份；健康启动后删除启动器 `.bak`。
6. 验收基线：新增 docs/mcp-real-douyin-acceptance.md，覆盖原生互动布局、隐藏恢复、音频挂起、直播、登录、DPI、锁屏、更新回滚和 30 分钟观察。

## 自动验证

- `git diff --check`：通过，仅有仓库现有 LF→CRLF 提示。
- VS Code diagnostics：0 error，0 warning。
- x64 完整构建：通过，包含 Bootstrap 与共享参数模块。
- x86 完整构建：通过，包含 Bootstrap 与共享参数模块。
- x64 `scripts/test.ps1`：69 项自测、smoke、live-smoke、settings-smoke、53 项 regression 全部通过。
- x86 `scripts/test.ps1`：69 项自测、smoke、live-smoke、settings-smoke、53 项 regression 全部通过。

## 尚需人工执行

自动测试使用本地合成页面，不能证明真实抖音 DOM、账号登录、真实音频、多显示器 DPI、Windows 锁屏或线上更新安装无问题。发布前必须按 docs/mcp-real-douyin-acceptance.md 执行相应人工项目并记录结果。
