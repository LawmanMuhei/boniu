# 清爽模式健康守卫误回退修复（滚动时自动退出清爽模式）

- 日期：2026-10-08
- 模块：`src/PageScripts.cs` - `BuildVisibility()` 注入的 `__boniuImmersiveHealthGuard`
- 现象：开启清爽模式后连续下滑视频，几秒后清爽模式自动失效，页面顶部出现「已恢复标准布局 · 点击重试清爽样式」提示，需要手动再开。

## 根因

旧判定把「活动视频」写死为 `[data-e2e="feed-active-video"]`，且只要取不到该节点、或该节点未覆盖视口中心，就直接记为一次异常：

```js
var active=document.querySelector('[data-e2e="feed-active-video"]');
var videos=active?active.querySelectorAll('video'):document.querySelectorAll('video');
var ok=coversCenter(active);          // active 为 null 时恒为 false
if(ok){...}                           // 只有 active 正常时才回退到内部 video 判断
misses=ok?0:misses+1;
if(misses<2)return;                   // 连续两次异常即回退
```

抖音推荐流是虚拟列表：滚动时 `feed-active-video` 会被回收、重建或改写，切换动画期间新旧视频都不覆盖视口中心。任一采样点落在该窗口即记一次异常，连续两次（约 1.5~3 秒）就触发安全回退；回退是闩锁式的（`pageStyleFallback=true` 后滚动不会自动重试），因此表现为「清爽模式自己退出，需要手动再开」。

## 修复

1. 判定改为「是否存在正在渲染（`display`/`visibility` 正常且尺寸大于 0）的 video 覆盖视口中心」，不再依赖 `feed-active-video` 标记；
2. 合并为单一循环：任一健康 video 命中即清零计数，只有「确实有 video 在渲染但无一覆盖中心」才计一次异常；
3. 阈值由连续 2 次提高到连续 3 次（约 4.5 秒）；
4. 新增 scroll / wheel / touchmove 监听（capture），滚动期间失败计数清零，切换过渡帧不会被累计；
5. 没有任何正在渲染的 video（加载、回收阶段）时不判定，保留原有「导航阶段不误回退」行为。

## 验证

- `get_diagnostics src/PageScripts.cs`：0 条诊断；
- `scripts/build.ps1 -TestsOnly -SkipSigning -OutputRoot artifacts/verification`：编译通过，`--self-test` **69 项全部通过**；
- `artifacts/verification/build/x64/波妞摸鱼.Tests.exe --regression-test`：**53 项回归全部通过**，其中
  - `healthy video switching does not trigger fallback`（6 轮设置往返与切视频不误回退）继续通过；
  - 布局损坏用例仍成立：把播放视频压到 10px 宽后，连续 3 次采样异常在 7 秒判定窗口内触发回退。
- 真实页面仍需人工复测：连续下滑 30 条以上视频，清爽模式应保持开启；手动破坏布局时仍能回退并显示提示。

