# UI/UX 评审：波妞摸鱼（对照真实截图）

日期：2026-09-21。方法：artifacts/verification/settings-x64.png 与 settings-x86.png（280×460 最小窗口下设置页的真实渲染）+ src/UiControls.cs、MainForm.Settings.cs、MainForm.Hotkeys.cs、MainForm.cs、MainForm.Updates.cs、MainForm.WebView.cs 精读。未做真人可用性测试；[已核实] 有代码或截图依据，[建议实测] 需上手复现。

## 总体印象

设计语言统一（深色 iOS 风卡片、胶囊分段器、拟物开关），微文案质量高（「已关闭；锁屏时仍会隐藏」「点击录入组合键」），快捷键录入的即时校验反馈是亮点；置顶/静音/设置按钮有绿色激活态，tooltip 动态携带快捷键。整体已优于多数个人工具。以下聚焦真正值得改的部分。

## P0. 老板键 × 模态对话框：摸鱼泄露路径

> **状态：已修复（2026-09-21）**：更新确认改两步式按钮；ShowOwnedMessage 与热键占用警告改行内横幅；HideWindow 增加 DismissOwnedPopups 清场安全网覆盖其余模态（危险确认、WebView 失败框）。实施与验收清单见 docs/mcp-bosskey-modal-fix.md。

- [已核实] 所有弹窗均为 MessageBox.Show(this, …) 模态且 owner=主窗：ShowOwnedMessage（src/MainForm.Settings.cs:635）、ShowUpdateConfirmation（src/MainForm.Updates.cs:71）、无边框模式警告（src/MainForm.cs:453）、WebView 启动失败（src/MainForm.WebView.cs:188）。
- 机制：RegisterHotKey 的 WM_HOTKEY 在模态消息循环中仍派发到主窗 WndProc（src/MainForm.cs:362 已处理）；HideWindow 只 Hide() 主窗。Win32 下 owner 隐藏不会连带隐藏 owned 弹窗——主窗消失后，对话框（标题往往写着「波妞摸鱼更新」「波妞摸鱼」）继续浮在屏幕上。
- 现有防护只挡了一半：模态期间 suppressInactiveHide=true 防的是「失焦隐藏」，未防「老板键隐藏」。
- 建议（推荐 A）：
  A. 老板键路径枚举并关闭/隐藏 OwnedForms 与活动弹窗，记录待恢复状态，ShowWindow 后在设置页状态文字里补提示；
  B. 全面去模态：更新确认改为内容区横幅+按钮（pageNotice 模式已存在），错误提示改非模态状态文字/短 toast；
  C. 最低成本兜底：HideWindow 发现模态弹窗时先将其关闭（等价取消）再隐藏，信息降级为设置页文字。
- 验收：打开更新确认框 → 按 Ctrl+Alt+D → 屏幕应完全无窗；恢复后可再次检查更新。

## P1. 视觉与排版

> 状态：已修复（2026-09-21）——对比度/危险色/字体阶梯见 docs/mcp-p1-ui-fix.md。

1. [已核实] 卡片小标题过小且对比度不足：「快捷键/行为/界面…」与右上水印用 7pt YaHei + TextTertiary #636366。对比度：#636366 on 纯黑 ≈ 3.6:1、on 卡片 #1C1C1E ≈ 2.8:1，低于 WCAG AA 小字 4.5:1。建议 10~11pt + TextSecondary（#98989D on #1C1C1E ≈ 5.6:1 达标）。
2. [已核实] 分段选择器选中态临界：白字 on #787880 ≈ 4.4:1，略低于 4.5。选中底色加深一档即可。
3. [已核实] 危险操作无危险色：「清除登录和网页缓存」与「打开数据目录」同款中性按钮。建议 clearDataButton 用调色板已有的 Destructive #FF453A 系（文字或描边），保留二次确认。
4. [已核实] 字体硬编码「Microsoft YaHei UI」+ 断号字号（20F/8F/7F）：建议改 SystemFonts 链，字号阶梯整理为 20/12/11/9。

## P1. 交互

> 状态：已修复（2026-09-21）——工具栏折叠溢出/拖拽/字形/文案/禁用/静音键/状态点见 docs/mcp-p1-ui-fix.md。

1. [截图可见] 最小宽度工具栏 8 键拥挤：280px 时按钮压到约 30px、间隙归 1~2px（LayoutToolbarButtons 压缩逻辑），顶部可拖拽区只剩边距——「拖顶部空白移动窗口」几乎无处可拖。建议：设置页标题区接入 DragWindow（已有实现）；窄窗收纳「后退/刷新」进溢出菜单；statusDot 让位。
2. [截图可见] 两个眼睛图标相邻易混：chromeButton(E8A7) 与 hideButton(E890)。隐藏窗口是核心操作，建议换更独特的字形（幽灵/向下箭头类），tooltip 已带快捷键可兜底。
3. [已核实] 快捷键说明文案不统一：前两行状态「当前组合键」，第三行「点击录入组合键」。统一为「点击修改/点击录入」，说明与状态分离。
4. [已核实] 快捷键只能改不能清空、静音键固定不可改：建议提供「禁用该快捷键」入口；静音键开放自定义（默认 Ctrl+Alt+M，冲突检测 ConflictsWithAnyRegistered 已具备）。
5. [已核实] 状态点无名称：StatusDot 7×7 三色无 tooltip/AccessibleName。建议 tooltip「页面状态：正常/加载中/出错」。

## P2. 键盘与可达性

- [已核实] RoundedButton TabStop=false，工具栏不可 Tab 到达；ToggleSwitch/SegmentedPicker 已有 AccessibleRole 且可 Tab，但全 UI 无焦点环绘制（OnPaint 无 focus visual），键盘用户会迷路。建议绘制 1px Accent 焦点矩形。
- [已核实] 滚轮步长 e.Delta/3（DarkScrollPanel）非系统习惯；建议每格 3 行的标准步长。
- [已核实] 7px 缩放把手不随 DPI（WndProc grip=7 未 Scaled）：高分屏过窄。建议 Scaled(7) 或 GetSystemMetrics(SM_CXSIZEFRAME+SM_CXPADDEDBORDER)。
- [已核实] FitLabel AutoEllipsis 截断后无 tooltip，窄窗长文案不可见全文。

## P2. 细节打磨

- ZoomFactor 随窗宽 0.44~1.0 联动是聪明做法，但无「锁定缩放」设置，拉宽窗口时字号突变；可加缩放锁。
- 无边框警告（MainForm.cs:453）与 WebView 失败框并入 P0 一并去模态。
- 模态改非模态后，ShowOwnedMessage 的 suppressInactiveHide 包裹逻辑可整体删除，代码更简。

## 做得好（保持）

- 开关/分段/滚动条自绘质感统一，Toggle 有按下/禁用/阴影旋钮细节；
- 快捷键录入：三类错误即时反馈 + Esc 取消 + CancelShortcutCapture 回滚重注册旧键；
- tooltip 动态带快捷键（「立即隐藏 (Ctrl+Alt+D)」）；
- hideButton 在无法恢复时自动禁用（防自锁），chromeButton 同理；
- 数据卡「登录和设置仅保存在本机」隐私微文案；
- ToggleSettings 对隐藏中的 WebView 延迟注入（防黑屏）注释显示深思熟虑。

## 验收清单（改动后）

1. P0 复现步骤全绿（模态中老板键 → 屏幕无残留窗）；
2. 280×460 最小窗 + 200% DPI 截图对比；
3. 纯键盘走查：Tab 到工具栏 → 设置行 → 全部可操作，焦点环可见；
4. Narrator 可念出所有开关、状态点、快捷键按钮；
5. 高对比度模式扫一遍文本对比度。

