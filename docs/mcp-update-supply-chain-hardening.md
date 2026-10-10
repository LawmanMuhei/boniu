# 更新供应链加固（主机白名单 / nupkg 哈希 / 发布 WhatIf）

日期：2026-10-10。范围：实施 docs/mcp-optimization-remaining.md「C. 安全与供应链」与 docs/mcp-next-optimization-roadmap.md「第二批」中三项未完成项：更新下载主机白名单、构建期 WebView2 包哈希校验、发布脚本 -WhatIf。同时重建 dist 使发布产物命名一致。本轮只改更新与脚本链路，未改页面适配、窗口状态或播放行为。

## 背景

三项都由静态复审提出，且截至本轮之前均**未落地**（已在源码中逐条核实）：

- `UpdateService.EnsureHttps` 只校验 `Uri.Scheme == https`，Release API 响应若被篡改或引向任意 HTTPS 主机仍会通过。
- `scripts/build.ps1` 已钉住 WebView2 版本 `1.0.4191.47`，但从 nuget.org 下载后不校验哈希，同版本可被替换字节。
- `scripts/publish.ps1` 直接 `gh release create`，无法只预览将上传的资产。

## 改动

### 1. 更新下载只接受 GitHub 主机

`src/UpdateService.cs`：`EnsureHttps` 替换为 `EnsureTrustedDownloadUrl`，在原 HTTPS 校验之上增加主机判定；新增可测试的 `IsTrustedDownloadHost`。接受 `github.com`、`githubusercontent.com` 及其子域，其余主机抛 `InvalidDataException`。

判定用带点后缀 `.githubusercontent.com` 匹配，因此 `evilgithubusercontent.com` 不通过、`github.com.evil.example` 不通过；`AssetUrl` 与 `HashUrl` 都走同一检查。

**边界**：该检查约束的是 Release API 响应给出的**初始地址**。HttpClient 仍由系统 HTTP 栈跟随 GitHub 自己发出的重定向（release 资产会跳到 `objects.githubusercontent.com` 等 CDN），未对每一跳单独判定。要伪造一个从 github.com 跳到攻击者主机的重定向需要控制 GitHub 自身的跳转，不在本威胁模型内；真正的完整性由配套 SHA-256 校验保证。

### 2. 构建期校验 WebView2 包哈希

`scripts/build.ps1`：新增 `$packageSha256`（当前 `f492bbf547d0da329553b6727435b677579b1e9f91cc9e4a1ad029366d5f23d0`，对应 `Microsoft.Web.WebView2.1.0.4191.47.nupkg`）与 `Assert-PackageArchive`。

- 首次下载后先校验、再解压；
- 已存在解压目录但缓存有 nupkg 时也校验，避免缓存被替换后静默复用；
- 不匹配即抛错并提示删除该文件后重建。

更新包版本时必须同时更新 `$packageVersion` 与 `$packageSha256`。

### 3. 发布脚本 -WhatIf

`scripts/publish.ps1`：`[CmdletBinding(SupportsShouldProcess = $true)]`，上传与显示标签之前调用 `$PSCmdlet.ShouldProcess`。

- `-WhatIf` 时**仍然真实构建并生成 dist 资产**（校验文件里的哈希必须来自真实文件），只跳过上传、不联网、不要求安装 gh；
- 打印目标 Release、8 个资产的名称、大小与 SHA-256，以及发布后会加显示标签的资产。

**实现坑（已修）**：`$WhatIfPreference` 是偏好变量，会被 publish.ps1 调用的 build.ps1 继承，导致 build.ps1 内的 `Remove-Item` / `New-Item` / `Copy-Item` 一并被跳过——第一次 `-WhatIf` 运行因此只编译了 EXE 而没有重建 `build\x64` 目录、也没有生成 `BoniuMoyu*.exe`，留下半新半旧的 dist。现改为：进入本地构建阶段前保存并置 `$WhatIfPreference = $false`，在 `finally` 中恢复，上传阶段的 ShouldProcess 才据此判定。

### 4. dist 产物命名一致

此前 `dist\波妞摸鱼*.exe` 为 2026-10-08 20:37 的重建产物，而自动更新用的 `dist\BoniuMoyu*.exe` 仍是当天 17:23 的旧副本，直接发布会造成新旧混搭。本轮以 `publish.ps1 -WhatIf` 完整重建 x64/x86，并重新生成 `BoniuMoyu*.exe`、各自的 `.sha256` 与带版本号的手动下载副本。

## 验证

全部在本工作区实际执行，非静态推断：

- `scripts\test.ps1 -Platform x64|x86`：5 个用例全部 exit 0；自测由 73 项增至 **78 项通过**（新增主机白名单正反例与 3 条离线用例），回归 **53/53 通过**。
- 离线更新测试的夹具主机由 `https://fixture.invalid/...` 改为 `https://github.com/...`，使既有下载/哈希/中断/超大/超时用例继续生效；新增用例覆盖非 GitHub 下载主机、伪装主机 `evilgithubusercontent.com`、非 GitHub 校验文件主机，并保留非 HTTPS 拒绝用例。
  这些用例在无网络桩下只有地址检查能产生 `InvalidDataException`：若先触达 handler 会变成 `HttpRequestException`，测试即失败，因此断言确实落在地址检查上。
- `scripts\build.ps1`：`WebView2 1.0.4191.47 archive SHA-256 verified.`，x64/x86 构建均通过。
- `scripts\publish.ps1 -Version 1.8.1 -WhatIf`：exit 0、未上传；核对 `波妞摸鱼.exe` / `BoniuMoyu.exe` / `BoniuMoyu-1.8.1-x64.exe` 三者哈希一致（x86 同理），4 个 `.sha256` 与对应 EXE 全部匹配。
- `scripts\check-version.ps1`：Version consistency OK: 1.8.1。

## 未完成 / 边界

- 无代码签名证书。SHA-256 sidecar 与 EXE 同信道发布，信道被攻破仍可同时替换；文档既有结论不变。
- 发布脚本只加了 `-WhatIf`，仍是"构建 + 上传"一体；未做上传失败回滚或幂等重跑。
- 子框架导航仍未设防（仅顶层 OnNavigationStarting），本轮未处理。
- 真实抖音页面人工验收仍未执行，见 docs/mcp-real-douyin-acceptance.md。
- 本轮未提交到任何 Git 仓库：本工作区是源码压缩包，不含 .git。
