# v1.11.0 实施交付与审查入口

日期：2026-10-27。实施依据：`mcp-v1.11-mobile-clarity-zoom-ui-plan.md`。没有推送、发布、安装更新或修改已有快捷方式；真实用户登录和设置未用于自动化验证。

## 已实现

1. 阶段 0 的在线手机身份探针、真实页面截图、设备信号、导航与 HTTP 状态、切换输入前后的视频身份记录。
2. 可选 Mobile / Desktop 模式、统一 UA 与 Client Hints / 触摸 / 视口、按模式选择入口、阻止 App 协议、导航设备日志、进程退出后的配置恢复。
3. Mobile 默认用户比例 100%；Desktop 自动适配可关闭，下限提高至 67%；处理窗口 DPI 变化并重新布局。
4. 原生 Ctrl+滚轮网页缩放、Ctrl+0 重置、50%~200% 范围、双模式独立持久化、设置按钮与 1.2 秒提示。
5. 靛紫主题、工具栏分组、分段器与开关、焦点环、120ms 共享计时器过渡、设置页排序与卡片、通知与高对比度滚动条。
6. 版本统一 1.11.0，README / 使用说明同步。保持 SDK 1.0.4191.47 与 C# 5 编译器，无新依赖。

## 探针对原方案的具体影响

- **默认仍为 Desktop**：手机身份可用滚轮及上下键切视频，但主站仍保留电脑布局；m.douyin.com/ 返回 404。没有确认登录兼容，不能启用“新老用户都默认手机”的分支。
- 使用主站手机推荐参数，必要时调用页面自身推荐导航；不新增域名白名单。
- 不启用鼠标转触摸、不注入滚轮转触摸脚本，因为实测原生滚轮/方向键可切换，触摸上滑未切换。
- Mobile 三个电脑布局开关与直播横屏置灰，清爽模式不套用电脑 CSS / 健康守卫；没有实测必需的 App 下载遮罩，不猜测隐藏选择器。
- 可选“优先最高画质”跳过，尚无稳定、实测的菜单选择器。
- 浅紫配白色小字对比度不足，选中分段使用同色系深紫，详见 UI 记录。

## 验证结果与产物

最终版本一致性 1.11.0；x64/x86 每种架构 134 项自测、87 项合成回归通过，5 个测试入口均 exit 0。相比基线增加 13 项自测、12 项回归；编译使用系统 .NET Framework C# 5 编译器。两个架构的独立 EXE 在根目录 `dist/`，未签名、未发布。

- `artifacts/verification/tests-x64.json`、`tests-x86.json`。
- `artifacts/verification/regression-x64.json`、`regression-x86.json`。
- `artifacts/verification/mobile-spike-inputs/`：在线探针 JSON、截图和导航日志。
- `artifacts/verification/ui-layout-*.png`：设置页顶/底，普通/窄窗/浮层工具栏。
- `artifacts/verification/before-ui-layout-*.png`：阶段 3 UI 基线的同方法对照。
- `artifacts/verification/ui-review-*.json`：实际窗口 DPI 与模拟布局口径。
- `artifacts/verification/ui-contrast.json`：17 个颜色组合计算。

artifacts 按仓库原规则 ignored，仅保留在本地，不随提交上传。用户提供的原方案文档保持原样。

## 明确未完成的人工验收

- 已有 Cookie 在手机身份下的登录沿用、扫码/验证码/密码登录。
- 同账号手机与电脑各 30 条推荐的竖屏比例统计；不能拿匿名会话的少数样本代替。
- 手机身份下的真实直播、点赞/评论等完整交互、长时间使用与真实音频表现。
- 真实 Ctrl+滚轮、触控板捏合的输入手感；当前回归检查的是浏览器缩放事件处理、比例保存和重置消息。
- 真实 144-DPI 显示器及多显示器迁移；150% 截图是 uiScale 布局模拟，未改变 Windows 显示设置。
- 真实渲染/浏览器进程崩溃恢复、同一视频码流分辨率前后对照。

## 分阶段记录

- [手机可行性探针](mcp-mobile-mode-spike.md)
- [访问身份与导航](mcp-mobile-mode-implementation.md)
- [清晰度与 DPI](mcp-clarity-dpi-implementation.md)
- [网页缩放](mcp-page-zoom-implementation.md)
- [UI、对比度和截图](mcp-ui-coordination-implementation.md)
