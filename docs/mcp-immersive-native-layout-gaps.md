# 清爽模式顶部与右侧空白修复

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`
- 依据：真实抖音页面 `--dom-probe` 与用户截图

## 现象与实测

清爽模式已不再强制裁剪视频，连续切换恢复正常，但隐藏顶部栏和右侧操作按钮后，原布局的占位仍然存在：

- 顶部：视频和列表从 `y=56px` 开始，视口上方保留 56 CSS px；
- 右侧：`[data-e2e=slideList]` 的计算样式为 `padding-right:60px`，视口宽 724px、实际视频/slide 宽 664px；
- 当前页面缩放为 0.63，所以截图中的物理空白约为顶部 35px、右侧 38px，与实测完全对应。

## 修复

清爽模式仅增加两条占位清理规则：

```css
#dark #douyin-right-container { padding-top: 0 !important; }
#dark [data-e2e="slideList"] { padding-right: 0 !important; }
```

没有设置播放器、`#slidelist`、feed slide 或 `video` 的宽高、定位、比例、`object-fit`、`overflow` 或 `transform`。视频显示和上下切换继续完全沿用抖音原生实现。

## 验证

- 修改前实测：活动视频和 feed slide 为 `x=0, y=56, width=664`，`slideList` 为 `padding-right=60px`；
- 修改后 `--dom-probe` 实测：活动视频、feed slide 和 `slideList` 均为 `x=0, y=0, width=724`，右内边距为 `0px`；
- 视口仍为 `724 × 949`，视频 `object-fit:contain`、slide 定位和列表 transform 均来自抖音原生样式；
- `get_diagnostics src/MainForm.cs`：0 条错误或警告；
- `scripts/build.ps1`：编译成功；
- `build/波妞摸鱼.Tests.exe --self-test`：25 项通过；
- 新产物：`dist/波妞摸鱼.exe`（1,779,200 字节）。
