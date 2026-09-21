# 清爽模式恢复互动按钮

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`

## 需求

清爽模式中继续显示抖音原生的头像、点赞、评论、收藏、转发和更多操作按钮。清爽模式只隐藏顶部导航、搜索、私信入口、弹幕和视频文案等页面装饰。

## 修改

从清爽模式隐藏清单中移除：

- `live-avatar`
- `video-avatar`
- `video-player-digg`
- `feed-comment-icon`
- `video-player-collect`
- `video-player-share`
- `video-play-more`

互动按钮恢复后，抖音原生 `padding-right:60px` 会在按钮右边额外留下整条黑色空白。最终方案保留所有互动按钮，同时在清爽模式中设置 `[data-e2e=slideList] { padding-right:0 }`，让视频区铺满窗口，互动按钮按原生定位叠放在视频右侧。顶部 56px 导航占位仍清零，列表满高规则继续保留。

设置中的独立“隐藏右侧互动区”开关仍保持原有语义：用户主动打开该开关时，互动区仍会隐藏。

## 验证

真实页面 `--dom-probe` 中以下控件均恢复为可见状态并具有有效尺寸：

- 头像：`video-avatar` / `live-avatar`；
- 点赞：`video-player-digg`；
- 评论：`feed-comment-icon`；
- 收藏：`video-player-collect`；
- 转发：`video-player-share`；
- 更多：`video-play-more`。

最终真实页面验证：`slideList`、feed 和活动视频的右内边距均为 0，宽度均扩展为完整的 720px；互动按钮保持可见，并从原来的约 `x=590` 向右移动到约 `x=650`，位于铺满宽度画面的右侧，不再留下额外黑条。代码诊断为 0 条错误或警告，27 项自测通过，构建成功。新产物为 `dist/波妞摸鱼.exe`（1,779,200 字节）。
