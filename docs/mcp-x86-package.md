# Windows 32 位（x86）打包与发布

- 构建命令：`scripts\build.ps1 -Platform x86`
- 本地输出：`dist\波妞摸鱼-x86.exe`
- Release 资产：`BoniuMoyu-x86.exe` 和 `BoniuMoyu-x86.exe.sha256`

x86 构建使用 `/platform:x86`，内置 NuGet 包的 `runtimes\win-x86\native\WebView2Loader.dll`。启动后组件解压到 `%LOCALAPPDATA%\MiniViewWebView2\App\1.8.0-x86`，不会覆盖 x64 组件目录。

自动更新根据 `IntPtr.Size` 选择架构资产，32 位程序不会下载或安装 64 位 EXE。x64 和 x86 发布文件分别计算 SHA-256，并沿用设置备份、健康启动检测和失败回滚。
