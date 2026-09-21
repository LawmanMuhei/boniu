# 清爽模式视频溢出修复记录（immersive video fit）

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`（页面元素可见性/清爽模式 CSS 注入）
- 产出：`dist/波妞摸鱼.exe`（1,782,784 字节，2026-09-21 08:45:48）

## 问题现象

进入清爽模式观看视频时：

1. 视频画面超出窗口可视范围；
2. 窗口底部会露出下一条视频的一部分；
3. 视频被强制 `width:100%;height:100%`，窗口比例与视频原始比例不一致时被拉伸变形。

## 根因分析

旧实现把容器硬写成 `100vw/100vh`，但存在三个漏洞：

1. 容器全部没有 `overflow:hidden`：抖音自己的 JS 按 resize 时量的尺寸摆放 slide，量高结果与窗口实际高度不一致时 slide 错位，下一条就从窗口底部漏出来；
2. slide 高度依赖 `feed-item` 等历史选择器，页面改版导致选择器失配时没有兜底；
3. `video` 被拉满父容器，等价于 `object-fit:fill`，直接破坏官方原始比例。

## 行为约定（与用户确认）

1. "官方比例" = 每条视频自己的原始宽高比；窗口比例不一致时整条视频等比缩放、完整展示，多余区域留黑边；只做放大/缩小，绝不拉伸、裁切或向外扩展；
2. 清爽模式保留底部播放进度条。

## 修复实现（三层防线 + 对齐守卫）

全部规则沿用 `#dark` 前缀把特异性抬到 id 级并加 `!important`：

1. 容器层：`#douyin-right-container` 与 `.parent-route-container`、`.route-scroll-container`、`[class*="route-container"]`、`#slidelist` 全部钉在窗口上（100vw/100vh）并加 `overflow:hidden`，超出窗口的内容一律裁掉；
2. slide 层：`#slidelist > *`、`[data-e2e="slideList"] > *`（直接子节点，防改版）加历史选择器 `feed-item/feed-video/feed-live/#sliderVideo/#slider-card`，统一正好一屏高 + `overflow:hidden` + 黑底，下一页从窗口底边开始，绝不露头；
3. 视频层：`#slidelist video` 等全部 `object-fit:contain` + 黑底（官方比例等比缩放，留黑边）；`#slidelist .xgplayer` 撑满所在 slide；
4. 对齐守卫：注入后分 6 波（间隔 350ms）触发 `window` 的 `resize` 让抖音 JS 重新量高；之后每秒抽查 slide 高度是否等于窗口高（阈值 2px），不等再触发 resize，连续 10 次仍不齐则停手；退出清爽模式或样式节点被移除时守卫自动退出。样式节点带 `data-immersive` 标记供守卫识别当前状态。

隐藏清单不变（顶部搜索/导航/右侧互动区/弹幕/视频信息等照旧），进度条不隐藏。

## 验证

- `get_diagnostics src/MainForm.cs`：0 条问题；
- 将注入 JS 还原（去掉 C# 拼接）后 `node --check`：语法通过；
- `scripts/build.ps1`：三段编译全部成功，产出 `dist/波妞摸鱼.exe`。

## 待人工验证

- [ ] 清爽模式下窗口底部不再露出下一条视频；
- [ ] 竖屏/横屏视频均完整、居中、按官方比例（多余区域黑边）；
- [ ] 底部进度条保留，上/下滑切换正常；
- [ ] 退出清爽模式恢复原布局；
- [ ] 更新方式：先退出正在运行的旧实例，再运行新的 `dist/波妞摸鱼.exe`（Bootstrap 按 SHA256 对比自解压 payload，内容不同会自动覆盖 `%LOCALAPPDATA%\MiniViewWebView2\App\1.6.0-x64`）。

## 如仍有问题的后续手段

以 `--dom-probe` 参数运行程序（`RunDomProbe`），会生成 `%TEMP%\boniu-dom.json`、`%TEMP%\boniu-dom-immersive.json` 与截图 `boniu-baseline.png`、`boniu-immersive.png`，可按真实 DOM 精修选择器。
