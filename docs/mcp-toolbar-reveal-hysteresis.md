# 清爽模式顶部悬停唤出工具栏：改为贴顶触发 + 迟滞

- 日期：2026-10-10
- 模块：`src/MainForm.cs`（常量）、`src/Logic.cs`（`WindowRules.ShouldRevealToolbar`）、`src/MainForm.Page.cs`（`UpdateImmersiveToolbar`）
- 需求：清爽模式下工具栏本来收起、光标移到顶部才临时露出。原来触发的范围太大，鼠标在画面偏上区域一晃工具栏就弹出来，希望改成**几乎贴到最顶边**才出来。

## 现状与根因

判定在 `UpdateImmersiveToolbar()`，只在清爽模式生效（`ImmersiveMode` 为真且未按 Ctrl+Alt+B 隐藏、窗口可见）：

```csharp
bool nearTop = cursor.X >= 0 && cursor.X <= ClientSize.Width
    && cursor.Y >= 0 && cursor.Y <= Scaled(ToolbarRevealHeight);   // 原为 44
```

`ToolbarRevealHeight` 原值 **44**，在默认 420×760 的窗口里是相当厚的一条带，因此"往上偏一点"就触发。而且进出用同一个阈值，没有任何迟滞。

## 改动

**1. 拆成两个阈值**（`src/MainForm.cs`）：

| 常量 | 值 | 含义 |
| --- | --- | --- |
| `ToolbarRevealHeight` | 6 | 唤出：光标进入距窗口顶部 6px 以内 |
| `ToolbarKeepHeight` | 44 | 保持：光标留在 44px 以内就不收；离开才收 |

工具栏实际占 `y ∈ [1, 37]`（`Padding` 为 1，`ToolbarHeight` 为 36），所以 44 能覆盖整条工具栏并留 7px 余量，鼠标可以正常下移到按钮上再点击。

**2. 迟滞 + 上甩兜底**（`WindowRules.ShouldRevealToolbar`，纯函数便于自测）：

```
唤出：cursorY ∈ [-keepHeight, revealHeight]
保持：已经唤出 且 cursorY ∈ [-keepHeight, keepHeight]
横向：cursorX 必须落在 [0, clientWidth]
```

两处非显然的设计：

- **迟滞**：进出阈值不同。否则工具栏一弹出、视频被推下去，指针往下移一点就立刻满足收起条件，会来回抖动，而且按钮点不到。
- **上边缘兜底**：`cursorY` 为负也算贴顶（下限 `-keepHeight`）。工具栏判定挂在 `pointerTimer` 上，窗口可见时每 **10ms** 采样一次；鼠标快速上甩时两次采样之间能移动 20~40px，会**整个跳过 6px 的窄带**，表现为"甩到顶了工具栏却不出来"。上限同样收在 44px 内，避免鼠标停在窗口上方别的窗口上时工具栏一直挂着。

**3. 横向超出窗口时既不唤出也不保持**，与改动前一致。

**4. 唤出时改为浮层覆盖，不挤压播放区**（`ToolbarOverlay` / `WindowRules.ShouldOverlayToolbar`）：

改动前 `LayoutWindow` 把工具栏高度算进布局，唤出时 `contentHost` 高度减少 36px。这不只是"视频往下挪一下"——WebView2 的尺寸真的变了，网页会收到 resize 并**重新排版一次**，所以每次贴顶/离开都伴随一次页面重排和视频位移。

现在只有"清爽模式下工具栏本来收起、因为悬停才露出来"这一种情况走浮层：`contentHost` 始终占满整个高度，`toolbar.BringToFront()` 把工具栏压在画面之上。于是播放区在**收起和唤出两种状态下尺寸完全相同** —— 视频一帧不动，网页也收不到 resize。

- 浮层成立的前提是控制层级：`webView` 是 `contentHost` 的子控件，而 `toolbar` 是 `contentHost` 的**兄弟**（`Controls.Add(contentHost)` 之后 `Controls.Add(toolbar)`）。子窗口盖不过父窗口的兄弟窗口，所以工具栏一旦在 `contentHost` 之上，画面不可能反过来盖住它。
- 正常模式、设置页、`Ctrl+Alt+B` 隐藏工具栏这三种情况仍按原样占位，避免工具栏长期盖住画面顶部。
- 副作用：「已恢复标准布局 · 点击重试清爽样式」提示条在 `contentHost` 的 y=8，工具栏浮现时会被压住。这是可自愈的——鼠标往下离开 44px 区域工具栏即收起，提示条恢复可见，而且此时光标位置（>6px）不会再触发唤出，可以正常点击。

## 验证

- `scripts\test.ps1 -Platform x64|x86`：5 个用例全部 exit 0；自测由 78 项增至 **91 项通过**，回归 55/55 通过。
- 新增 8 条自测覆盖迟滞的边界：中段不触发、贴顶 6px 唤出、顶边唤出、上甩越过上边缘仍唤出、离窗口上方过远不唤出、已唤出时在工具栏区域保持、离开 44px 后收起、横向超出不唤出也不保持。
- 新增 5 条自测覆盖浮层判定：清爽模式悬停唤出用浮层、未唤出时不浮层、正常模式仍占位、彻底隐藏工具栏时不浮层、设置页打开时仍占位。
- `scripts\publish.ps1 -Version 1.8.1 -WhatIf`：重建 x64/x86，dist 三种命名哈希一致，4 个 `.sha256` 全部匹配。

## 未验证 / 边界

- 迟滞的实际手感（6px 是否合适、44px 余量是否够）**只能在真机上凭手感受确认**，自动化只覆盖了判定函数的边界，没有模拟真实鼠标轨迹。
- **浮层是否真的盖住画面必须真机确认**。控制层级上成立（见上），但没有在真实 WebView2 上目视验证过。若工具栏被画面盖住、点不到按钮，方案改为独立无边框置顶小窗口。
- 若觉得仍偏灵敏，调小 `ToolbarRevealHeight` 即可；若觉得上甩时偶尔不出现，优先确认 `-keepHeight` 兜底是否被真机 DPI 缩放影响。
- 用 `Ctrl+Alt+B` 隐藏工具栏是另一种模式，顶部悬停本来就不唤出（只有快捷键能恢复），本次未改动。
- 受控沙箱下无法运行完整测试套件：设置自测会在 `%TEMP%` 做原子替换（`File.Replace`），沙箱允许新建临时文件但拒绝其中的删除/替换步骤，会报假失败（`SettingsStore.Save` → `UnauthorizedAccessException`）。正式验证需在非受限模式运行。`-OutputRoot` 指向工作区根下的**新**目录可以绕开 `artifacts`/`build`/`dist` 的写入限制，但绕不开这一条。
- 本轮未提交到任何 Git 仓库：本工作区是源码压缩包，不含 .git。
