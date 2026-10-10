# 清爽模式健康守卫误回退修复（打开评论面板自动退出清爽模式）

- 日期：2026-10-10
- 模块：`src/PageScripts.cs` - `BuildVisibility()` 注入的 `__boniuImmersiveHealthGuard`；`src/MainForm.Page.cs` - `OnWebMessageReceived`
- 现象：开启清爽模式看视频时点开评论，清爽模式自动失效，页面出现「已恢复标准布局 · 点击重试清爽样式」，需要手动再开。

这是 docs/mcp-immersive-health-guard-false-fallback.md（滚动误回退）的同族问题：健康守卫把抖音自己的交互重排当成了注入样式损坏。

## 根因

守卫要求「正在渲染的 video 覆盖视口中心」，四个条件同时成立才算健康：

```js
return cs.display!=='none'&&cs.visibility!=='hidden'&&r.width>innerWidth*.4&&r.height>innerHeight*.4&&
  r.left<=innerWidth/2&&r.right>=innerWidth/2&&r.top<=innerHeight/2&&r.bottom>=innerHeight/2;
```

打开评论面板时抖音会为面板腾出空间，把播放器缩小或推到一侧。此时画面仍然完整、可用、面积不小，只是因为不再居中而**不覆盖视口中心**，于是每次采样都记一次异常；连续 3 次（约 4.5 秒）后触发闩锁式回退（`pageStyleFallback=true` 后不会自动重试），表现为「清爽模式自己退出」。

窗口越小越容易命中：默认窗口是 **420×760**（`WindowRules.DefaultSize`），评论面板占据右侧后视口中心很容易落到播放器之外。迷你小窗（340×600）更明显。

## 修复

判定标准从「画面是否覆盖视口中心」改为「**可见画面占视口的比例**」：

1. 新增 `visibleRatio(el)`：取 video 与视口的交集面积 ÷ 视口面积；`display:none` / `visibility:hidden` / 零尺寸 / 完全在视口外都返回 0；
2. 阈值 `MIN_VISIBLE_RATIO = 0.25`：正常铺满、迷你窗口、评论面板展开后的缩小画面都远高于此；被压成窄条或移出视口的真实损坏接近 0；
3. 多个 video 取最大比例，避免虚拟列表里离屏的 video 覆盖主播放器的判定；
4. 新增 `overlayOpen()`：抖音评论面板（`data-e2e="comment-list"` / `comment-panel` / `video-comment"` 或 `commentPanel` / `comment-panel` / `CommentPanel` / `commentContainer` 类名）展开且本身是一块可见大区域（宽 > 25% 视口、高 > 40% 视口）时，清零失败计数并跳过本轮判定。这一步不依赖几何尺寸，因此即使窗口窄到播放器被压到阈值以下也不会误回退。

同时把守卫回退时的**实测值**写进消息与 `run.log`：

```
immersive-health fallback-to-native-layout best=0.02 vw=420 vh=760 videos=2
```

C# 侧改为按 revision 前缀精确匹配，带 `|` 后缀的消息才算同一代次，过期代次依旧忽略。真实页面若再出现误回退，`run.log` 能直接说明当时的画面比例与视口尺寸，不必再靠推测。

### 已知取舍

在抖音**始终显示评论**的页面（视频详情页）上，守卫会一直处于跳过状态，不再为该页面提供自动回退保护。这是有意的：那里的布局本来就与推荐流不同，此前正是这类页面容易误回退；用户仍可手动切换清爽模式。

## 验证

- `scripts\test.ps1 -Platform x64|x86`：5 个用例全部 exit 0；自测 **78 项通过**，回归由 **53 项增至 55 项全部通过**。
- 新增两个回归用例（合成夹具，非真实抖音页面）：
  - `narrowed player does not trigger fallback`：把画面缩到 45% 宽（不覆盖视口中心、但比例仍达标）连续观察 6 秒，不得回退；
  - `comment panel does not trigger fallback`：展开夹具评论面板并把画面压到 20% 宽（低于 0.25 阈值）连续观察 6 秒，靠面板抑制不得回退。
- 原有 `fallback notice visible outside settings` 仍通过：把 playing 压到 10px 宽（无面板）后仍能在判定窗口内触发回退，**真实损坏的检出能力没有被削弱**。
- 这两个新用例对旧代码是失败的：45% 宽时 `r.right = 0.45×视口宽 < 视口宽/2`，中心点判定不成立，旧代码会在 4.5 秒内回退。
- `scripts\publish.ps1 -Version 1.8.1 -WhatIf`：重建 x64/x86，dist 三种命名（波妞摸鱼 / BoniuMoyu / 带版本号）哈希一致，4 个 `.sha256` 全部匹配。**修复已进入 dist 中的 EXE。**

## 未验证 / 边界

- **未在真实抖音页面上复现与验证**：本轮无法打开真实评论面板交互，结论来自守卫判定规则的代码分析与合成夹具回归。请在实际使用中确认：开评论后清爽模式应保持开启。
- 评论面板的选择器是依据抖音常见 `data-e2e` 与类名约定给出的，未在真实 DOM 上核对。若真实面板命名不同，抑制分支不会命中，此时仍由 `MIN_VISIBLE_RATIO` 生效；若两种机制都没兜住，`run.log` 里的 `best=` 会给出实际比例，据此可直接定位。
- 阈值 0.25 与 4.5 秒判定窗口沿用原有保守取向，未做真机调参。
- 本轮未提交到任何 Git 仓库：本工作区是源码压缩包，不含 .git。
