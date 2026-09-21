# 清爽模式自定义快捷键

- 日期：2026-09-21
- 模块：`src/MainForm.cs`、`src/SettingsStore.cs`、`src/Logic.cs`、`src/Program.cs`

## 功能

- 默认组合键：`Ctrl + Alt + F`；
- 按一次进入清爽模式，再按一次退出；
- 设置页“快捷键”卡片提供“进入/退出清爽模式”录入按钮；
- 点击按钮后按下包含 Ctrl、Alt、Shift 或 Win 的组合键，Esc 取消；
- 与隐藏窗口、隐藏边框、静音快捷键冲突时拒绝保存；
- 系统保留组合键或已被其他程序注册的组合键拒绝保存并显示原因；
- 自定义结果写入 `settings.json` 的 `ImmersiveShortcut`，重启后重新注册；
- 注册失败时启动阶段显示明确警告，设置页保持“快捷键不可用，请重新设置”状态。

## 本次修复

原有主要链路已经实现，但“恢复程序默认设置”只恢复了隐藏窗口和隐藏边框快捷键，没有重新读取清爽模式默认组合键，随后保存时会把旧自定义值写回。本次补上 `ImmersiveShortcut` 的重新解析与赋值，确保恢复默认后确实回到 `Ctrl + Alt + F`。

同时补充设置按钮说明、启动注册失败提示、README 说明，以及默认值与冲突关系自测。

## 验证

- `get_diagnostics src`：0 条错误或警告；
- `scripts/build.ps1`：编译成功；
- `build/波妞摸鱼.Tests.exe --self-test`：27 项通过；
- `build/波妞摸鱼.Tests.exe --smoke-test`：退出码 0；
- 新产物：`dist/波妞摸鱼.exe`（1,779,712 字节）。
