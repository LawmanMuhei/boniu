# 清爽模式切换下一条视频黑屏修复

- 日期：2026-09-21
- 模块：`src/MainForm.cs` - `BuildPageVisibilityScript()`
- 现象：从设置返回后的第一条视频正常，向下切换到第二条及后续视频时只显示黑屏。

## 根因

清爽模式把 `#slidelist` 同时设置为固定一屏高、`max-height:100vh` 和 `overflow:hidden`。但该节点是抖音纵向视频虚拟列表的移动轨道，其原生高度和位移负责把下一条 slide 移入视口。列表被限制并自裁剪后，第一条仍在裁剪区内，后续条目则无法正常进入可见区域。

同时，旧规则对每个 slide 强制 `top:0!important`，覆盖了虚拟列表用于定位后续条目的原生 `top`，进一步造成 slide 重叠或目标视频位于错误位置。

## 修复

1. 继续由最外层容器和路由视口执行 `100vw/100vh + overflow:hidden`，确保窗口外内容不会露出；
2. `#slidelist` 只约束宽度，不再覆盖其原生高度、最大高度、`top` 或 `transform`，并允许轨道内容溢出；
3. 每个 slide 仍保持一屏尺寸和内部裁剪，但不再覆盖原生 `top/transform` 定位；
4. 视频继续使用 `object-fit:contain`，保持原始比例。

这样裁剪职责位于稳定的外层视口，而移动轨道可以正常切换第一条、第二条及后续视频。

## 验证

- `get_diagnostics src/MainForm.cs`：0 条错误或警告；
- `scripts/build.ps1`：编译成功；
- `build/波妞摸鱼.Tests.exe --self-test`：25 项通过；
- `build/波妞摸鱼.Tests.exe --smoke-test`：退出码 0；
- 新产物：`dist/波妞摸鱼.exe`（1,783,296 字节）。

连续切换真实抖音视频涉及登录态和在线页面，需使用新产物人工复测至少三条视频。
