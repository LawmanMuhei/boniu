# v1.11 阶段 0：手机网页可行性探针

日期：2026-10-27。基线 main @ f3e7ac5。测试使用独立匿名 WebView2 用户目录，未读取、复制或修改真实登录数据。浏览器版本与完整结果见 `artifacts/verification/mobile-spike-inputs/result.json`，导航记录见该目录 `logs/run.log`。截图和 JSON 均由真实在线页面产生，不是合成夹具。

## 结论与实施决策

采用方案的第二分支：**保留 Desktop 默认，Mobile 作为可选访问身份**。手机信号确实生效，进入推荐页后滚轮及方向键可以切换实际视频，但当前主站仍提供含桌面导航的页面，并未变成独立的 App 式手机 H5。设置必须注明这一限制，不得声称“手机版推荐更多短视频”或“登录兼容已验证”。

`m.douyin.com/` 当前返回 404；不能将它写为正式入口。主站首次打开会经前端路由进入精选页，需要使用页面自身的推荐入口。手机身份点击后实际路由为 `https://www.douyin.com/?is_from_mobile_home=1&recommend=1`，可作为手机模式候选入口；正式适配将保留用户可见的推荐导航，不依赖清除登录数据来切换。

## 三个入口与跳转

| 请求 | 实际落地 | 结果 |
| --- | --- | --- |
| `https://www.douyin.com/` | `/jingxuan` | HTTP 200 后 SourceChanged 前端路由进入精选，仍含精选、推荐、搜索、投稿等桌面布局 |
| `https://www.douyin.com/?recommend=1` | `/jingxuan` | HTTP 200 后前端路由跳转；点击导航中的推荐可进入推荐流 |
| `https://m.douyin.com/` | 原地址 | HTTP 404，正文 `404 Not Found / TLB`，无 video |

此轮未出现页面级导航到外部域名或 App 协议调用；页面存在外部链接，不等于发生导航。不扩大生产白名单。探针静默取消外部协议、下载和新窗口，拒绝非抖音 HTTPS 导航，并记录被拦 URL。

## 设备仿真与交互

完整应用 Android Chrome UA、Client Hints metadata、5 个触摸点、鼠标转触摸、mobile 视口；UA 主版本取自实际内核 155。页面实测：`mobile=true`、`platform=Linux armv8l`、`maxTouchPoints=5`、`pointer:coarse=true`。

- 手机推荐流：触摸滚动前后视频 ID 未变；滚轮后视频 ID 从 `356919578` 变为 `-1133101570`，方向键后变为 `1456240348`，证明这些输入确实切换了视频。因此无需额外将滚轮映射成触摸手势。正式模式不启用鼠标转触摸，以保持桌面控件点击行为；触摸能力仿真仍保留。
- 推荐导航使用页面自身链接点击，成功进入推荐页；真实鼠标转触摸的按钮点击完整性没有人工验收。
- 0/0 device metrics 下窗口从 420×724 调到 360×640，`innerWidth` 从 739 变成 633，证明随窗口变化，不需要额外 resize 下发尺寸。
- 页面 meta viewport 自带 `initial-scale=0.5`，所以 ZoomFactor=1 也可能出现较宽的 CSS 视口。不能仅凭 UA 或 ZoomFactor 就推断已经得到手机布局或高清码流。

## 清晰度、登录与推荐统计边界

同一窗口、同一显示环境下：桌面 ZoomFactor=0.58 时 DPR=0.6612、innerWidth=636；设为 1 后 DPR=1.14、innerWidth=369。设备仿真并未人为提高 DPR，Mobile 下为 1.14。截图中的视频源已出现 1920×1080、1440×1080、720×1280 等尺寸，但不同视频不能作为码流提升的对比证据；精选页的 videoWidth=0 表示尚无可用视频元数据。

登录弹层在桌面阶段出现，包含扫码、验证码和密码登录选项；Mobile 推荐页仍存在“登录”入口。本轮未扫码或填写凭据，**旧 Cookie 登录沿用、手机身份登录、登录后直播均未验证**。

**同账号各 30 条竖屏比例统计未完成**：当前使用匿名隔离会话，不能把不同视频、预加载 video 或同一视频循环播放当作同账号 30 条样本。不能据此得出手机身份改善推荐的结论，后续需要用户登录后补测。当前可观察的手机流样本包含约 10 秒、282 秒、1373 秒的横屏视频，已足以否定“换 UA 必然获得短视频”的承诺，不能用于统计效果。

未发现必须隐藏的 App 下载遮罩；探针文本正则会误命中“每次打开抖音默认进入的页面”，它不是 App 引导，不纳入 CSS。手机模式清爽样式因此先不注入隐藏规则，并跳过桌面健康守卫；直播 DOM 未验收，手机模式直播自动横屏置灰。

## 截图与复现

- `artifacts/verification/mobile-spike-inputs/desktop-058.png` 与 `desktop-100.png`：网页缩放对照。
- `mobile-1.png`、`mobile-2.png`、`mobile-3.png`：三个入口。
- `mobile-feed-ready.png`、`mobile-feed-wheel.png`、`mobile-feed-keyboard.png`：实际推荐流及切换。
- 每张截图同名 JSON 保存设备、页面文本、video 尺寸及可见性；`result.json` 汇总导航、HTTP 状态和输入前后视频身份。

```powershell
.\scripts\build.ps1 -OutputRoot artifacts/verification -TestsOnly -SkipSigning -Platform x64
& '.\artifacts\verification\build\x64\波妞摸鱼.Tests.exe' --mobile-probe --probe-output "$PWD\artifacts\verification\mobile-spike-recheck"
```

阶段 0 编译通过；x64/x86 各 124 项自测通过、75 项合成回归通过，5 个测试入口均 exit 0。版本一致性仍为基线 1.10.1；正式功能从阶段 1 统一升至 1.11.0。探针仅显式调试参数启动，不影响普通启动路径。没有推送或发布。
