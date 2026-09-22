# P1 视觉与交互修复（九项）

日期：2026-09-21。范围：docs/mcp-ui-review.md 的「P1. 视觉与排版」4 项与「P1. 交互」5 项。全部修改在本地工作区完成，未提交、未发布、未签名；dist/ 未动。P2 未做。

## 视觉与排版

1. 卡片小标题/水印对比度：AddCard 标题与 brandLabel 由 7pt + TextTertiary（对卡片约 2.8:1）改为 UiFonts.Caption 11pt + TextSecondary（对卡片约 5.9:1，达 WCAG AA 小字 4.5:1）。（src/MainForm.Settings.cs）
2. 分段选择器选中态：SegmentedPicker.SelectedColor 由 (120,120,128) 加深为 (112,112,120)，白字对比度 4.38:1 → 4.91:1 达标。（src/UiControls.cs）
3. 危险操作危险色：CreateSettingsActionButton 的 danger 分支增配 Destructive #FF453A 描边（RoundedButton 新增 BorderColor，OnPaint 绘 1px 圆角描边；文字保留 Destructive 红），保留二次确认。（src/UiControls.cs、src/MainForm.Settings.cs）
4. 字体阶梯：新增 UiFonts（SystemFonts.MessageBoxFont 字体链 → Microsoft YaHei UI → GenericSansSerif；字号阶梯 20/12/11/9 = Hero/Body/Caption/Micro）。替换全部硬编码「Microsoft YaHei UI」（20F/9F/8.5F/8F/7F）：页标题 20、行标题 12、卡片标题/按钮/控件 11、状态微文案 9。图标字体 Segoe MDL2 Assets 不变。（src/UiControls.cs、src/MainForm.cs、src/MainForm.Settings.cs）

## 交互

1. 最小宽工具栏拥挤与拖拽区：窄窗（280px 最小宽即触发）把「后退/刷新」收纳进「更多（⋯）」溢出菜单（深色 ContextMenuStrip），statusDot 让位隐藏，顶部留白恢复可拖拽；设置页标题区（「设置」与右上水印）接入 DragWindow。（src/MainForm.cs、src/MainForm.Settings.cs）
2. 两个眼睛图标易混：hideButton 字形 E890 改为 E70D（向下箭头类），与 chromeButton 区分；tooltip 仍动态携带快捷键。（src/MainForm.cs）
3. 快捷键说明文案统一：状态行统一「点击修改」（已绑定）/「点击录入」（未绑定）；不可用态保留「快捷键不可用，请重新设置」；tooltip 统一「名称 (组合键)；点击修改/点击录入」。清除「当前组合键」「点击录入组合键」混用。（src/MainForm.Settings.cs、src/MainForm.Hotkeys.cs）
4. 快捷键禁用与静音键自定义：录入中按 Del/Backspace 禁用该快捷键（提示「正在录入，Esc 取消 · Del 禁用」；空串持久化；ParseShortcut 语义：null→默认、空串→禁用、非法→默认；按钮显「未设置」。禁用窗口键沿用安全路径：停用鼠标移出自动隐藏并保留托盘恢复入口，但不弹「已被占用」警告）。AppSettings 新增 MuteShortcut（默认 Control+Alt+M），设置页新增「静音/恢复声音」行；window/chrome/mute/immersive 四目标统一录入、冲突检测（ConflictsWithAnyRegistered）与持久化。（src/SettingsStore.cs、src/MainForm.cs、src/MainForm.Settings.cs、src/MainForm.Hotkeys.cs）
5. 状态点可访问性：StatusDot 内置 tooltip 与 AccessibleName（「页面状态：正常/加载中/出错」，StaticText 角色），State 切换即时更新并随控件释放。（src/UiControls.cs）

## 测试侧修正（产品行为不变）

- FitsInsideCard 改为文档空间比较：DarkScrollPanel（AutoScroll）滚动会把偏移烘焙进子控件 Location（客户坐标），而 card.Bounds 是文档坐标；换算后检查结果与滚动位置无关，约束不减弱。触发面：LayoutIsConsistentAcrossScales 在 1.5/2 倍 uiScale 下的滚动残留状态（P1 新增行高/字号改变滚动量后暴露）。（src/MainForm.Settings.cs）
- ToolbarLayoutIsConsistent 跳过 !Visible 按钮：与 FitsInsideCard 的 !Visible 语义一致；折叠态下隐藏的后退/刷新不再以陈旧 bounds 误报重叠/越界。（src/MainForm.Settings.cs）

## 测试

- scripts/test.ps1 -Platform x64 与 -Platform x86 全绿：Version consistency OK 1.7.0；自测 63 项通过；smoke / live-smoke / settings-smoke 通过（含 1/1.25/1.5/2 倍 uiScale 布局一致性、滚动后重排、滚动态改尺寸重入收敛）；回归 53 项通过。报告：artifacts/verification/tests-x64.json、tests-x86.json。
- 真实渲染核对：artifacts/verification/settings-x64.png（280×460 最小窗、折叠工具栏含 ⋯ 溢出按钮与 ⌄ 隐藏键、新字号阶梯、行内卡片对齐正常）。

## 人工验收清单

1. 280px 最小宽：工具栏为 ⋯/置顶/静音/设置/边框/⌄/✕，⋯ 菜单含「后退」「刷新」；拖顶部空白可移动窗口。
2. 设置页：拖「设置」标题或右上水印可移动窗口；卡片小标题与水印清晰可读。
3. 分段选择器选中项白字清晰；「清除登录和网页缓存」为红字红描边。
4. 快捷键行：已绑定显组合键且状态「点击修改」；录入中按 Del 后显「未设置」+「点击录入」；「静音/恢复声音」行可改录 Ctrl+Alt+M 等组合，冲突组合被拒。
5. 悬停状态点出现「页面状态：…」；隐藏键为向下箭头字形，tooltip 带当前组合键。

## 未改动与边界

- P2 未做（工具栏 TabStop/焦点环、滚轮三行步长、7px 缩放把手 DPI、FitLabel 截断 tooltip、ZoomFactor 锁定、suppressInactiveHide 包裹清理）。
- 无边框警告行内化、更新确认两步式已随 P0 完成（见 docs/mcp-bosskey-modal-fix.md）。
- dist/ 现有 EXE 为旧构建；发布走 scripts/publish.ps1 流程。

