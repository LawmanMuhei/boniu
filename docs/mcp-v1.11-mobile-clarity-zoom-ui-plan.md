# v1.11 优化方案：手机版模式 / 清晰度 / 滚轮缩放 / UI 协调

日期：2026-10-27。基线：main @ f3e7ac5（v1.10.1）。本文是交给执行方（Codex）的实施说明，完成后由 Claude 审查。
每一节写清「根因 → 改法 → 涉及文件 → 验收」。标注 **[先验证]** 的地方必须先做探针确认，不能凭假设写死。

---

## 0. 硬性约束（先读）

1. **编译器是 .NET Framework 4.8 自带的 `csc.exe`（scripts/build.ps1:42），只支持 C# 5。**
   禁用：`$"..."` 字符串插值、`?.` / `??=`、`=>` 表达式体成员、`nameof`、`out var`、元组、`using static`、本地函数、模式匹配。
   沿用现有写法：`delegate { ... }`、`string.Format`、显式类型。写完必须实际跑 `scripts\build.ps1` 编过。
2. WebView2 SDK 固定 1.0.4191.47（build.ps1 校验 nupkg 哈希）。不要升级 SDK，下面用到的 API 都已存在：
   `CoreWebView2Settings.UserAgent`、`CallDevToolsProtocolMethodAsync`、`AddScriptToExecuteOnDocumentCreatedAsync`、
   `LaunchingExternalUriScheme`、`ProcessFailed`、`WebView2.ZoomFactorChanged`。
3. JSON 只有 `System.Web.Script.Serialization.JavaScriptSerializer`；CDP 参数用 `Dictionary<string, object>` 序列化，不要手拼字符串。
4. 代码风格：partial 类按职责拆分（新功能放新 partial 文件），中文注释说明「为什么」，密度与现有文件一致。
5. 版本号统一升到 **1.11.0**：唯一来源 `src/AppVersion.cs`，然后同步 app.manifest、README、使用说明，`scripts\check-version.ps1` 必须通过。
6. 每个阶段结束都要跑：`scripts\check-version.ps1`、`scripts\test.ps1 -Platform x64`、`scripts\test.ps1 -Platform x86`（需非受限模式，见 docs/mcp-project-overview.md「验证状态」）。
7. 每个阶段一个独立提交（中文提交信息，风格同 git log），不要推送、不要发布。
8. 在 docs/ 下为每个阶段补一份 `mcp-*.md` 记录（沿用现有文档风格：做了什么、为什么、怎么验证、哪些没验证）。

---

## 根因总览（已在代码中核实）

| 问题 | 根因 | 位置 |
| --- | --- | --- |
| 1. 被识别成电脑、推荐偏长视频 | WebView2 默认 UA 是 Windows 桌面 Edge（含 `Edg/`），`navigator.userAgentData.mobile=false`、`Sec-CH-UA-Mobile: ?0`、`Sec-CH-UA-Platform: "Windows"`，无触控点；入口是桌面站 `https://www.douyin.com/?recommend=1`。桌面站推荐流本身就偏横屏/中长视频。 | src/MainForm.cs:18、src/MainForm.WebView.cs:23-33 |
| 2. 画面/文字不清晰、看着累 | `WindowRules.CalculateZoomFactor` 把页面按窗宽缩到 **0.44~1.0**：默认 420 宽时 ZoomFactor=0.58。后果有三：① 文字缩到 58%，小字发虚；② Chromium 的页面缩放会同步降低 `window.devicePixelRatio`（100% 屏上变成 0.58），抖音按 DPR 选封面/头像素材，拿到的就是低分辨率图；③ 播放器「智能清晰度」按「播放器像素尺寸 × DPR」选档，档位被压低。 | src/Logic.cs:50-55、src/MainForm.cs:578-581、src/MainForm.cs:307 |
| 3. 无法自定义缩放 | `IsZoomControlEnabled=false`，且每次 `Resize` 都调用 `UpdateZoom()` 把缩放强制写回按宽度算出的值。 | src/MainForm.WebView.cs:32、src/MainForm.cs:307 |
| 4. UI 不够协调 | 强调色是 iOS 绿 `#30D158`，激活态底色 `#17402A`，与应用图标的靛紫 `#6258DF/#8179F2` + 暖金 `#FFBE45` 毫无关系；灰阶来自 iOS 系统色、与工具栏 `#202020` 两套体系混用；工具栏 9 个 30×28 按钮平铺、无分组；不处理 DPI 变化（只在 OnLoad 读一次 DpiX）。 | src/UiControls.cs:16-41、src/MainForm.cs:215-227、src/MainForm.cs:388-402 |

---

## 阶段 0 [先验证]：手机版可行性探针（必须先做，产出决定阶段 1 的细节）

抖音的手机网页（H5）功能可能被大幅阉割（强制「打开 App」、不能连续刷、不能登录等）。**在写正式功能之前**，先做一个只在 `--dom-probe` 类调试参数下生效的探针，回答下面的问题，并把结果写进 `docs/mcp-mobile-mode-spike.md`：

1. 用阶段 1 的完整手机仿真（UA + Client Hints + 触控 + mobile 视口）分别打开以下入口，记录**最终落地 URL、重定向链、页面截图**：
   - `https://www.douyin.com/`
   - `https://www.douyin.com/?recommend=1`
   - `https://m.douyin.com/`
2. 落地页能否**连续刷推荐视频**？靠什么切换（触摸上滑 / 滚轮 / 键盘↑↓）？
3. 桌面版扫码登录留下的 Cookie，在手机版里**是否仍是登录态**？手机版自己能否登录？
4. 是否出现「打开 App」遮罩/横幅/弹窗，对应的选择器是什么？
5. 是否会跳转到 `*.douyin.com` 以外的域名（例如 `iesdouyin.com`、`douyinvod.com` 页面级导航）或自定义协议（`snssdk1128://`、`intent://`、`aweme://`）？——在 `OnNavigationStarting` 被拦的 URL 先加一行 `Diagnostics.Log("navigation blocked: " + uri)`，探针跑完看日志。
6. 视频清晰度：H5 播放器有没有清晰度菜单？`<video>` 的 `videoWidth/videoHeight` 是多少？与桌面版同窗口对比。
7. 刷 30 条推荐，统计竖屏（videoHeight > videoWidth）比例，与桌面版同账号对比——**这就是"推荐变短视频"是否成立的证据**，写进文档。

**决策门**：
- H5 能连续刷 + 能保持登录 → 按阶段 1 实现，**新装用户默认手机版**，老用户升级后也切到手机版（用户诉求就是这个），设置里可切回电脑版。
- H5 能刷但不能登录/功能残缺 → 仍实现阶段 1，但**默认保持电脑版**，手机版作为可选项，并在设置说明文字里写明限制。
- H5 基本不可用 → 停下来，把探针结论报告回来，不要硬做。备选方向（届时再定）：iPad/平板 UA 走桌面站的竖屏布局。

> 注意：换成手机身份只改变"设备信号"。推荐内容同时受账号历史影响，不保证立刻全是短视频——验收看第 7 条的统计，不看主观感觉。

---

## 阶段 1：手机版模式（站点模式切换）

### 1.1 设置项

`src/SettingsStore.cs` 的 `AppSettings` 新增：

```csharp
// 访问模式：Mobile = 以手机身份访问抖音手机网页；Desktop = 原有电脑网页。
public string SiteMode { get; set; }
```

- 构造函数默认值按阶段 0 的决策门定（"Mobile" 或 "Desktop"）。
- 在 `SettingsStore.Load` 的规范化段落里加 `WindowRules.NormalizeSiteMode`：只接受 `"Mobile"`/`"Desktop"`，其他值回退默认值。
- 在 `Logic.cs` 里加 `internal enum SiteMode { Desktop, Mobile }` 和解析函数，业务代码用枚举，不要到处比字符串。

### 1.2 设备仿真（新文件 `src/MainForm.DeviceProfile.cs` + 纯逻辑放 `src/Logic.cs` 或新 `src/DeviceProfile.cs`）

**为什么不能只改 UA**：`Settings.UserAgent` 只改 UA 字符串，`navigator.userAgentData.mobile`、`Sec-CH-UA-Mobile`、`Sec-CH-UA-Platform`、`navigator.maxTouchPoints`、`(pointer: coarse)` 媒体查询仍然是桌面值，前后矛盾的信号可能让站点仍判成电脑或布局错乱。要用 CDP 一次性把这些信号统一。

推荐身份：**Android Chrome**（与 WebView2 的 Chromium 内核一致，特性检测不会露馅）。

```
Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Mobile Safari/537.36
```

- `{major}` 取自 `environment.BrowserVersionString` 的主版本号（如 "130.0.2849.68" → 130），保证 UA 声称的版本与真实内核一致。解析失败时用一个写死的常量兜底。
- UA 构造写成纯函数 `DeviceProfile.BuildMobileUserAgent(string browserVersion)`，便于自测。

**应用顺序**（封装成 `async Task ApplyDeviceProfileAsync()`，必须在首次 `webView.Source = ...` 之前完成）：

| 步骤 | 调用 | 作用 |
| --- | --- | --- |
| 1 | `core.Settings.UserAgent = ua`（Desktop 模式恢复为初始化时缓存的默认 UA） | 兜底：任何没被 CDP 覆盖到的请求也带手机 UA |
| 2 | `Emulation.setUserAgentOverride` `{userAgent, acceptLanguage:"zh-CN,zh;q=0.9", platform:"Linux armv8l", userAgentMetadata:{brands, fullVersionList, platform:"Android", platformVersion:"14.0.0", architecture:"", model:"Pixel 8", mobile:true}}` | 统一 `navigator.userAgentData` 与 Client Hints 请求头 |
| 3 | `Emulation.setTouchEmulationEnabled` `{enabled:true, maxTouchPoints:5}` | `maxTouchPoints`、`ontouchstart` |
| 4 | `Emulation.setEmitTouchEventsForMouse` `{enabled:true, configuration:"mobile"}` | 鼠标拖拽变成触摸滑动，H5 的上滑切视频才能用 |
| 5 | `Emulation.setDeviceMetricsOverride` `{width:0, height:0, deviceScaleFactor:0, mobile:true}` | 启用 mobile 视口语义（meta viewport、overlay 滚动条）。width/height=0 表示跟随窗口，DSF=0 表示用真实 DPI |

- Desktop 模式：依次调用 `Emulation.clearDeviceMetricsOverride`、`setTouchEmulationEnabled{enabled:false}`、`setEmitTouchEventsForMouse{enabled:false}`、`setUserAgentOverride` 恢复为默认 UA（不带 metadata）。
- **[先验证]** `setDeviceMetricsOverride` 的 0/0 是否真的随窗口缩放：拖动改变窗口大小后执行 `innerWidth`，若不跟随，改为在 `ResizeEnd` 时（以及 Resize 防抖 150ms）用 DIP 宽高重新下发。
- **[先验证]** 鼠标点击在 `setEmitTouchEventsForMouse` 下仍能正常点按钮/点赞/评论；如果点击失效严重，去掉第 4 步，改为 1.4 的滚轮映射方案切视频。
- CDP 覆盖跟随页面目标生效，但渲染进程崩溃重建后可能丢失：订阅 `core.ProcessFailed`（目前项目**没有**处理），在 `RenderProcessExited`/`BrowserProcessExited` 后重新 `ApplyDeviceProfileAsync()` 再 Reload。
- 每次 `NavigationCompleted` 后执行一个轻量探针（一行 JS 返回 JSON）：`navigator.userAgent` 是否含 `Mobile`、`navigator.userAgentData && navigator.userAgentData.mobile`、`navigator.maxTouchPoints`、`matchMedia('(pointer:coarse)').matches`、`devicePixelRatio`、`innerWidth`、当前 ZoomFactor。写入 `Diagnostics.Log("device-profile ...")`；与当前模式不一致时重新应用一次（每次导航最多一次，防止循环）。

### 1.3 入口与导航守卫

- `DouyinUrl` 拆成 `DesktopHomeUrl` 与 `MobileHomeUrl`，后者按阶段 0 的结论填写；所有引用点（MainForm.WebView.cs:162、MainForm.Settings.cs:628 等）改为按模式取值的 `HomeUrl` 属性。
- 运行中切换模式：保存设置 → `await ApplyDeviceProfileAsync()` → `core.Navigate(HomeUrl)`。
- 导航白名单 `WindowRules.IsDouyinUrl`：**只在阶段 0 证明必需时**才增加域名（例如 `iesdouyin.com`），同样要求 HTTPS + 精确/子域匹配，并补自测用例（含伪造域名 `iesdouyin.com.evil.com` 必须拒绝）。
- 新增 `core.LaunchingExternalUriScheme += (e.Cancel = true)`：手机页会尝试唤起 App（`snssdk1128://` 等），默认会弹系统确认框，必须静默取消并记日志。
- `OnNewWindowRequested` 不变（已只允许抖音域名）。

### 1.4 手机版下的切视频（滚轮 / 键盘）**[先验证后决定是否需要]**

H5 推荐流通常只认触摸上滑，桌面用户习惯滚轮。若阶段 0 发现滚轮不能切视频：

- 用 `AddScriptToExecuteOnDocumentCreatedAsync` 注入一个仅在 Mobile 模式生效的脚本：监听 `wheel`（`ctrlKey=false` 时），400ms 节流，`chrome.webview.postMessage({type:'boniu-swipe', dir: deltaY>0?'next':'prev'})`；同理把 ↑/↓ 键映射过去。
- 宿主在 `OnWebMessageReceived` 里处理，调用 CDP `Input.synthesizeScrollGesture`：`{x: 视口中心, y: 视口中心, yDistance: next ? -0.6*innerHeight : 0.6*innerHeight, gestureSourceType:"touch", speed: 1200}`。
- `Ctrl+滚轮` 不得被这个脚本拦截（留给阶段 3 的缩放）。
- 消息来源校验：只处理 `e.Source` 为抖音域名的消息，`type` 不认识的直接忽略。

### 1.5 桌面专属功能在手机版下的处理

`PageScripts.BuildVisibility` 里的选择器（`#dark`、`douyin-navigation`、`slideList` 等）和清爽模式健康守卫全部针对桌面 DOM，在手机页上要么无效、要么会误触发「布局异常 → 回退」提示。

- Mobile 模式下：不注入桌面版样式；「隐藏左导航/顶部/右栏」三个开关在设置页置灰，状态文字写「仅电脑版可用」。
- 清爽模式在 Mobile 下改为注入一份**手机版专用**的最小 CSS：只隐藏阶段 0 找到的「打开 App」横幅/遮罩/下载引导，不动播放器；健康守卫在 Mobile 下不运行（或者单独写一份按手机 DOM 判定的，二选一，写进文档）。
- 直播自动横屏：阶段 0 顺带看手机页直播 DOM；如果 `LiveProbeScript` 判不出来，Mobile 模式下该开关置灰并注明。
- 迷你小窗、贴边隐藏、老板键、静音暂停（`MediaScripts` 基于 `<video>` 元素）应与模式无关，回归测试要覆盖到。

### 1.6 设置页

「界面」或新建「浏览」卡片最上方加一行分段选择器：**访问模式 [手机版 | 电脑版]**（复用 `SegmentedPicker`），下方小字说明：「手机版推荐更多竖屏短视频，画面更清晰；部分电脑版布局开关将不可用。」切换立即生效（重载页面）。

### 1.7 测试

- `Program.cs` 自测：UA 构造（版本号解析、异常版本号兜底）、`NormalizeSiteMode`、`HomeUrl` 按模式取值、白名单新增用例。
- `MainForm.Regression.cs`：在合成页上切到 Mobile，断言探针返回 `mobile=true`、`maxTouchPoints>0`、`pointer:coarse=true`；切回 Desktop 断言全部恢复。设置页 smoke test 的布局断言需覆盖新增的一行。

---

## 阶段 2：清晰度

### 2.1 去掉"按宽度缩小页面"（核心修复）

- **Mobile 模式：页面缩放恒为用户缩放（默认 100%），不再按窗宽缩放。** 手机页本身就是为 360~430 CSS 像素宽设计的，420 宽窗口正好合适，`devicePixelRatio` 恢复为真实 DPI，文字、封面、播放器档位都会回到正常清晰度。
- Desktop 模式：保留自适应能力，但改为**可关闭**并提高下限：
  - 新设置 `DesktopAutoFit`（默认 true，保持老行为可预期），关闭后恒为用户缩放。
  - 自适应下限从 0.44 提到 0.67（低于这个比例文字已不可读）；窗口过窄时宁可出现桌面页的横向布局拥挤，也不再把整页缩成 0.44。同步更新 `Program.cs:133-134` 的自测期望。
- `UpdateZoom()` 改为：`effective = userZoom × (desktop && autoFit ? fitFactor : 1.0)`，结果再 clamp 到 [0.25, 5.0]（WebView2 的合法区间）。

### 2.2 诊断与验证

- 1.2 中的 NavigationCompleted 探针要记录 `devicePixelRatio` 与 `<video>` 的 `videoWidth×videoHeight`，审查时用 run.log 对比改前改后。
- 在文档里给出同一窗口尺寸、同一 DPI 下：改前（ZoomFactor 0.58）与改后（1.0）的 DPR、首个视频分辨率、截图对比。

### 2.3 DPI 变化（多显示器 / 改缩放比例）

- 当前只在 `OnLoad` 读一次 DpiX（MainForm.cs:388），窗口拖到不同 DPI 的显示器后，自绘工具栏/设置页按旧比例绘制，发虚或尺寸不对。
- 在 `WndProc` 处理 `WM_DPICHANGED (0x02E0)`：按 lParam 给出的建议矩形 `SetBounds`，然后 `RefreshUiScale()`、`LayoutWindow()`、`LayoutSettingsPanel()`、`ApplyWindowShape()`，全部控件 `Invalidate()`。注意与贴边/吸附逻辑的 `WM_MOVING` 不要互相干扰（用 `SetBoundsProgrammatically` 现有方法）。
- `RefreshUiScale` 改用 `NativeMethods.GetDpiForWindow(Handle)`（Win10 1607+，项目只支持 Win10/11，可以直接用；加 `EntryPointNotFoundException` 兜底回 DpiX）。

### 2.4 （可选，P3）播放器清晰度偏好 **[先验证]**

阶段 0 若发现播放器有清晰度菜单，且「智能」档在小窗下明显偏低：加设置「清晰度：自动 | 优先最高」，「优先最高」时在视频切换后由页面脚本找到清晰度菜单选第一档。**必须基于实测的 DOM**，找不到菜单就静默放弃，不能报错、不能反复点击。若无法稳定实现，本条跳过并在文档中说明，不要硬写。

---

## 阶段 3：滚轮缩放（缩放网页内容，不改变窗口大小）

**交互约定**：
- 普通滚轮 = 抖音自己的切视频，不动。
- **Ctrl + 滚轮** = 缩放网页内容（文字、按钮、画面一起变大变小），窗口尺寸与工具栏尺寸都不变。
- **Ctrl + 0** 恢复 100%。
- 范围 50%~200%；每次缩放后在画面上方短暂显示「缩放 110%」，约 1.2 秒后自动消失（复用 `pageNotice` 的临时通知机制，单独用一个更短的计时器，不要改 noticeTimer 的 7 秒）。
- 手机版、电脑版**各自记忆**缩放比例：新设置 `MobileZoomPercent`、`DesktopZoomPercent`（int，默认 100，规范化到 50~200）。

**实现（推荐方案 A：使用 WebView2 原生缩放）**：

1. `core.Settings.IsZoomControlEnabled = true`，让 Chromium 原生处理 Ctrl+滚轮 / 触控板捏合。
2. 订阅 `webView.ZoomFactorChanged`：
   - 用一个 `applyingZoom` 布尔标记区分"程序自己设的"和"用户操作的"，程序设置时直接 return。
   - 用户操作时：`userZoom = webView.ZoomFactor / fitFactor`（Desktop 自适应时）或 `= webView.ZoomFactor`（其他情况），clamp 到 [0.5, 2.0]，若越界则立即写回边界值；保存到对应模式的设置（`ScheduleSettingsSave` 防抖）；显示缩放提示。
3. `UpdateZoom()`（Resize 时被调用）使用上面的 `userZoom` 计算，不再覆盖用户的选择。
4. Ctrl+0：`AreBrowserAcceleratorKeysEnabled=false` 时 Chromium 不会处理，需要自己处理——注入脚本监听 `keydown`（ctrlKey && key==='0'）发 `postMessage({type:'boniu-zoom-reset'})`，宿主重置。

**备选方案 B**（仅当 A 与手机仿真冲突，例如 `setDeviceMetricsOverride` 下原生缩放失效或视口错乱时）：保持 `IsZoomControlEnabled=false`，注入脚本监听 `wheel` 且 `ctrlKey` 时 `preventDefault()` 并 `postMessage({type:'boniu-zoom', delta})`，宿主按 10% 步进设置 ZoomFactor。选了哪个方案、为什么，写进文档。

**界面入口**：
- 设置页「界面」卡片加一行「页面缩放」：`[−] 100% [+]` + 「重置」（按钮步进 10%）。
- 工具栏不新增按钮（已经很挤）。

**测试**：自测覆盖 clamp/规范化与「fitFactor × userZoom」的计算；回归测试模拟 `ZoomFactor` 被外部改变后 Resize，断言用户缩放不被覆盖、设置值已更新、两种模式互不影响。

---

## 阶段 4：UI 美化与协调

目标：与应用图标（靛紫 + 暖金 + 深海军蓝）同一套视觉语言；工具栏更轻、更有层次；不改变任何功能和快捷键。

### 4.1 设计令牌（重写 `UiPalette`，保留现有字段名作为别名或一次性替换全部引用）

| 令牌 | 现值 | 新值 | 用途 |
| --- | --- | --- | --- |
| Page | `#000000` | `#0E0F14` | 设置页底（带一点蓝调，与图标的 #20243A 同族） |
| Shell / Toolbar | `#171717` / `#202020` | `#14151C` / `#181A22` | 窗体与工具栏 |
| Card | `#1C1C1E` | `#1C1E28` | 卡片 |
| CardElevated（新） | — | `#242734` | 输入框、快捷键框、分段器底 |
| Separator | `#38383C` | `#2C2F3D` | 分隔线 / 1px 描边 |
| TextPrimary | `#FFFFFF` | `#F2F3F7` | 正文（纯白在深底上刺眼） |
| TextSecondary | `#98989D` | `#A4A8B8` | 说明文字（对 Card 对比度 ≥ 4.5:1，需实际算一遍） |
| TextTertiary | `#636366` | `#717589` | 仅用于非关键水印 |
| Accent | `#30D158` 绿 | `#7B72F0` 靛紫 | 开关开启、分段选中、焦点环 |
| AccentHover / Pressed | 绿系 | `#8E86F5` / `#6258DF` | |
| ToolbarActive | `#17402A` 深绿 | `#2D2A55` | 工具栏激活态底（置顶/静音/迷你） |
| ToolbarActiveGlyph（新） | — | `#B9B3FF` | 激活态图标颜色 |
| Highlight（新） | — | `#FFBE45` 暖金 | 仅用于"有新版本"等少量提示点缀 |
| Destructive | `#FF453A` | `#FF5A5F` | 危险操作 |

要求：所有文字/背景组合对比度 ≥ 4.5:1（小字）、≥ 3:1（图标）。在 docs 里列出实际计算结果。

### 4.2 工具栏

- 分组与留白：左侧「状态点 + 后退 + 刷新」，右侧分两组：「置顶 · 静音 · 迷你」｜「设置 · 隐藏边框 · 隐藏 · 关闭」，组间 8px、组内 2px，中间用 1px×14px 的 `Separator` 竖线（窄窗折叠时去掉竖线）。保持现有窄窗折叠到「更多」菜单的逻辑。
- 图标：Win11 优先 `Segoe Fluent Icons`，不存在时回退 `Segoe MDL2 Assets`（码位两者兼容）；字号 10pt → 10.5pt，按钮 30×28 → 30×28 不变但圆角 8 → 6。
- 关闭按钮悬停改为 `#E5484D` 实底 + 白色图标（现在的 `#8C2B1C` 偏脏）。
- 激活态（置顶/静音/迷你）：底色 `ToolbarActive` + 图标 `ToolbarActiveGlyph`，不要再用绿色。
- 工具栏底部加 1px `Separator` 发丝线，与内容区分开；清爽模式浮层状态下改为半透明感（WinForms 无真透明，可用比 Toolbar 稍亮的 `#1E2029` 加底部阴影线模拟）。
- 悬停/按下状态加 120ms 颜色过渡（一个共享 Timer 做线性插值即可，不要每个按钮一个 Timer）。

### 4.3 设置页

- 标题区：「设置」20pt Bold → 18pt Semibold；品牌行改为「小图标 16px + 波妞摸鱼 v1.11.0」，放在标题下方同一行左侧。
- 卡片：圆角 12，内边距 14，卡片间距 12，卡片 1px `Separator` 描边；卡片标题 11pt `TextSecondary`，放在卡片外上方（iOS 分组列表风格，现有已接近，统一数值即可）。
- 卡片顺序调整为使用频率：**浏览**（访问模式、页面缩放）→ 行为 → 界面 → 快捷键 → 数据与更新。
- 开关 `ToggleSwitch`：开启色用 Accent；旋钮加 1px 阴影（已有，保留）。
- 分段选择器：选中块用 Accent 实底 + 白字；未选中文字 `TextSecondary`。
- 快捷键按钮：底色 `CardElevated`，录入中描边 Accent（替换现在的深绿 `ShortcutCapturing`）。
- 键盘焦点环：所有可 Tab 控件获得焦点时绘制 1.5px Accent 圆角描边（docs/mcp-ui-review.md P2 遗留项）。
- 「清除登录和网页缓存」保留危险色描边样式，与新 Destructive 色同步。

### 4.4 其他

- 页面通知 `pageNotice`：圆角 10、底色 `#242734` 95%、左侧 3px Accent 竖条；缩放提示用同一样式的小号版本，水平居中。
- 窗体缩放把手 7px 未随 DPI 缩放（docs/mcp-ui-review.md P2 遗留），改成 `Scaled(7)`。
- 不引入任何图片资源或第三方 UI 库；全部继续自绘。

### 4.5 验收

- `--settings-smoke-test` 的布局断言全部通过（280×460 最小窗口、滚动、跨缩放比例一致性）。
- 在 100%、150% 两种 DPI 下各截图：工具栏（正常 / 窄窗折叠 / 清爽浮层）、设置页顶部和底部，放到 `artifacts/verification/` 并在文档引用。审查会逐张对比改前截图。

---

## 交付清单（审查时逐项核对）

1. 阶段 0 探针文档（含截图、重定向链、竖屏比例统计、登录态结论、选择器）。
2. 阶段 1~4 各一个提交 + 一份 docs 记录。
3. 版本 1.11.0 一致性通过；README 与「波妞摸鱼-使用说明.txt」补充：访问模式、Ctrl+滚轮缩放、Ctrl+0 重置。
4. x64/x86 `scripts\test.ps1` 全通过，自测与回归用例数量较 v1.10.1（121 / 75）有相应增加，报告 JSON 保留。
5. 明确列出**未验证项**（真实抖音手机页长时间使用、登录、直播、多显示器 DPI 迁移等），不要写成"已验证"。

## 不要做的事

- 不升级 WebView2 SDK、不换编译器、不引入 NuGet 依赖。
- 不在 `Move` 事件里回写窗口位置，不改拖动实现（见 docs/mcp-edge-drag-snap.md）。
- 不为了"更像手机"去伪造 GPU/WebGL/Canvas 指纹等与布局无关的信号；只做让手机页正确渲染所需的那几项仿真。
- 不推送、不发布、不改 `scripts/publish.ps1`。
