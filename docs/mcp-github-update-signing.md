# GitHub Releases 更新、回滚和可选代码签名

- 当前版本：1.7.0
- 更新源：`https://github.com/LawmanMuhei/boniu`
- 检查策略：每次启动检查，发现新版后提示用户确认

## 发布资产约定

每个正式 Release 必须同时包含：

- `BoniuMoyu.exe`
- `BoniuMoyu.exe.sha256`

发布资产使用 ASCII 文件名，避免 GitHub 上传接口清理中文文件名；客户端下载后仍会替换用户原来的“波妞摸鱼.exe”。

首次发布前需要先向空仓库推送至少一个 commit，并执行 `gh auth login`。之后运行 `scripts/publish.ps1` 即可创建标签和 Release。

客户端只接受 HTTPS 地址，并在安装前比较 SHA-256；缺少校验文件或哈希不匹配时拒绝安装。预发布和草稿 Release 不参与更新。

## 安装和回滚

更新包下载到 `%LOCALAPPDATA%\\MiniViewWebView2\\Updates\\<版本>`。开始安装前备份 `settings.json`，外部 PowerShell 安装器等待旧进程退出后备份并替换启动 EXE。新版本必须在 30 秒内写入健康标记，否则恢复 `.bak` 并重新启动旧版本。详细结果记录在本地 `logs\\update-install.log`。

## 代码签名

`scripts/build.ps1` 支持但不强制代码签名。没有证书时正常构建并提示跳过。支持：

- `BONIU_SIGN_PFX` + `BONIU_SIGN_PASSWORD`
- `BONIU_SIGN_THUMBPRINT`（证书已安装到 Windows 证书存储）

签名会应用到内层应用和最终单文件启动程序，并使用 SHA-256 与 RFC 3161 时间戳。没有正式证书时不能消除 Windows 的“未知发布者”提示。
