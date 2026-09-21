# 清爽模式改为沿用抖音原生播放器布局

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`

## 用户确认的行为

清爽模式只负责隐藏导航、搜索、互动按钮、弹幕和视频信息等页面装饰。播放器外框比例、视频素材在播放器内的适配方式，以及上下切换视频的虚拟列表布局均由抖音原页面决定。

不再按每条视频素材的宽高比修改播放器，也不再由程序强制 `16:9`、`9:16`、`100vw/100vh` 或 `object-fit`。用户选择的是“沿用抖音原布局、完整显示”，而不是程序自定义裁剪。

## 删除的干预

1. 删除 `#douyin-right-container` 的固定定位、全窗口尺寸和 `overflow:hidden`；
2. 删除路由容器及 `#slidelist` 的强制宽高、最大尺寸和溢出规则；
3. 删除每个 slide 的强制一屏尺寸、黑色背景和内部裁剪；
4. 删除 `.xgplayer` 与 `video` 的强制宽高和 `object-fit:contain`；
5. 删除连续六次 `resize` 和每秒执行的对齐守卫，并在脚本执行时清理旧守卫。

这可避免应用 CSS 覆盖抖音虚拟列表和播放器计算，从根源上消除切换下一条后被裁掉或只剩黑底的问题。

## 验证

- `get_diagnostics src/MainForm.cs`：0 条错误或警告；
- `scripts/build.ps1`：编译成功；
- `build/波妞摸鱼.Tests.exe --self-test`：25 项通过；
- `build/波妞摸鱼.Tests.exe --smoke-test`：退出码 0；
- 新产物：`dist/波妞摸鱼.exe`（1,776,128 字节）。
