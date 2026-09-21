# 清爽模式底部空白修复

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`
- 依据：用户截图与真实抖音页面 `--dom-probe`

## 核对结果

用户判断属实。顶部和右侧占位清除后，视口高度为 949 CSS px，但内部 `[data-e2e=slideList]` 高度只有 937px，当前 `[data-e2e=feed-item]` 高 938px，并带有 `margin-bottom:12px`。当前页面缩放为 0.63，12 CSS px 对应约 7.6 个物理像素，与截图底部红框中的空白一致。

## 修复方式

清爽模式下：

```css
#dark [data-e2e="slideList"] { height: 100% !important; }
```

最终只让抖音自己的 slide 轨道视口继承 `#slidelist` 的完整高度。曾尝试将 feed 条目的原生 `margin-bottom:12px` 清零，但真实滑动后会破坏虚拟列表的页距，使下一条视频从底部露出；该修改已撤销，12px 页距继续由抖音维护。没有改动 `video`、播放器、`object-fit`、横纵比例、`top`、`transform` 或 `overflow`。

## 验证

- 修改后真实 DOM：视口、`#slidelist` 和 `[data-e2e=slideList]` 高度均为 949px，起点均为 `y=0`；
- 当前 feed slide 为 `y=0, height=950, margin-bottom=12px`，本体覆盖 949px 视口底边；下一条从 `y=962` 开始，位于视口外 13px；
- 后续条目依次从 `y=1924`、`y=2886` 开始，原生页距和虚拟列表步长保持一致；
- 宽度仍为 724px、右内边距为 0，顶部和右侧修复保持有效；
- `get_diagnostics src/MainForm.cs`：0 条错误或警告；
- `scripts/build.ps1`：编译成功；
- `build/波妞摸鱼.Tests.exe --self-test`：25 项通过；
- 新产物：`dist/波妞摸鱼.exe`（1,779,200 字节）。
