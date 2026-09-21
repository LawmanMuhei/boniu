# MCP Bridge 连接报告 - shuncode-bridge

**连接状态**: ✅ 已连接  
**时间**: 2026-09-19  
**URL**: `https://vii-priority-taken-discussed.trycloudflare.com/mcp/180809e7e7caab50d51bc87c36aafe42`  
**Server**: `shuncode-bridge v0.7.5`  
**Protocol**: `2025-11-25` (client兼容 `2025-03-26`)  
**Transport**: Streamable HTTP - POST `Accept: application/json, text/event-stream`, `mcp-session-id` header

## 1. 连接方式

Python SDK 已验证可用：

```python
from mcp.client.streamable_http import streamable_http_client
from mcp import ClientSession

URL = "https://vii-priority-taken-discussed.trycloudflare.com/mcp/180809e7e7caab50d51bc87c36aafe42"

async with streamable_http_client(URL) as (read, write):
    async with ClientSession(read, write) as session:
        init = await session.initialize()
        print(init.server_info)
        tools = await session.list_tools()
```

本地封装见 `../mcp_client.py`，提供 `MCPBridge` 类，封装了常用调用。

curl 探测也可用，但必须正确提取 `mcp-session-id` 头（注意 `access-control-allow-headers` 中也包含该字符串，需用 `^mcp-session-id:` 精确匹配）。

## 2. 远程工作区现状

**项目名**: 波妞摸鱼 (MiniViewWebView2) - WebView2 原生 Windows 小窗

```
[DIR] assets  - 图标 16-1024px, .ico, .svg
[DIR] scripts - build.ps1 (使用 .NET Framework 4.8 编译, 下载 WebView2 SDK)
[DIR] src     - 7个 C# 文件
  - AssemblyInfo.cs
  - Bootstrap.cs
  - Diagnostics.cs
  - Logic.cs
  - MainForm.cs
  - Program.cs
  - SettingsStore.cs
[FILE] README.md
[FILE] 波妞摸鱼-使用说明.txt
[FILE] app.config (.NET 4.8)
[FILE] app.manifest
[FILE] THIRD-PARTY-NOTICES.txt
```

无 `docs/` 目录（需新建），无 `node_modules`，纯 C# + WebView2。

## 3. 使用规则 (来自 server instructions)

### 核心原则
- **操作范围**: 整个本地 Windows 设备，优先当前 ShunCode 打开的文件夹，但可检查同 PC 其他路径（用 `run_command` 访问外部）
- **文件工具作用域**: `list_directory`, `find_files`, `read_files`, `read_image`, `search_files`, `apply_patch` 仅限 workspace；外部路径需 `run_command` + `ls/find/grep`
- **交付物同步**: 所有分析/审计/计划/评审必须写入 `docs/` 下，命名 `docs/mcp-<topic>.md`，LF 换行，无尾随空格，内部引用改为 repo 路径。探测脚本、机器可读基线、日志留在 `docs/` 外，除非用户要求

### 并行策略
- 独立工具调用可并发，Bridge 并行执行，吞吐量随并发数线性提升，批量并行远快于串行
- 仅在真实依赖时串行：编辑前必须读，命令输入依赖前一结果
- 不要并行写同一文件
- Host 有并发上限，超限排队，不会失败，无需自限流
- `cancel_command`, `get_command_output`, `send_command_input` 永不排队，可随时检查长任务

### 任务协调
- 多步工作用 `set_todos`，发送完整有序 todo 列表，每次状态变更都发
- 最多一个 `in_progress`，使用稳定 ID
- Todo 保持目标级别，不要每个工具调用一个 todo
- `report_progress` 用于当前正在做的事，不是持久状态；当只有一个 in_progress 时自动关联
- 完成前发送终态快照并等待确认，全部 `completed` 才算完成；空列表清空任务状态
- `update_plan` 是 `set_todos` 的兼容形式（Codex 风格）

### 工具使用指导
- 优先语义导航 (`lsp`) 而非宽泛文本搜索定位符号
- 空 LSP 结果不代表符号不存在
- `search_files` 精确文本，`lsp` 符号/定义/引用/类型
- patch 失败（stale/上下文不匹配）后重读再试
- 小而聚焦的 patch，带足够唯一上下文
- 有意义编辑后跑 `get_diagnostics` 和相关测试
- 长任务中定期 `report_progress`，但不要每个调用都报

## 4. 可用工具清单 (15个)

### 文件发现
- **list_directory**: 列目录，depth 1或2，max_entries 200(最大500)，include_hidden/no_ignore 可选
- **find_files**: 按 glob 找文件，不搜内容，patterns 数组批量，path 相对 workspace，默认大小写不敏感，跳过忽略目录和隐藏路径，按修改时间倒序，候选最多5000，返回默认100最多500
- **search_files**: 搜内容，字面默认，is_regex 可选，smart-case，path 可缩小范围，include/exclude glob，context_lines 0-5，max_results 100(500)，max_matches_per_file 20(100)
- **read_files**: 读 UTF-8 文本，可批量20个，支持 start_line/end_line (1-based inclusive)，大文件自动截断返回 next_start_line，超大文件必须显式范围
- **read_image**: 读图片 PNG/JPEG/GIF/WebP/BMP/SVG/ICO，返回尺寸、宽高比、格式、MIME、大小、base64 data URI

### 编辑与诊断
- **apply_patch**: Codex 风格多文件 patch，`*** Begin Patch` / `*** End Patch`，支持 `*** Update File:`, `*** Add File:`, `*** Delete File:`, `*** Move to:`，hunk 用 `@@` + 空格/ -/ + 前缀，上下文必须精确唯一，支持 expected_versions sha256 防 stale，预检后原子安装，返回 canonical diff
- **get_diagnostics**: 读 VS Code 和语言服务诊断，含未保存编辑器状态

### 语义导航
- **lsp**: 代码语义导航 workspace/document symbols, go-to-definition, references, implementations, hover/type，含 provider_state, project_anchor 等元数据

### 命令执行 (Windows PTY)
- **run_command**: 在 ShunCode 管理的持久 PTY 中跑 Bash (PortableGit bash.exe --noprofile --norc)，无 PowerShell 回退，工具链含 git/grep/sed/awk/find/curl，Windows 盘用 /c/...，需 Windows 特性显式调 powershell.exe，idle 2h 关闭，cwd 省略则复用最近 idle 终端，background 可选，返回随机 command_id，支持 pipeline_exit_codes
- **get_command_output**: 用 command_id 读新输出/状态/exit_code，next_offset 避免重复
- **cancel_command**: 软中断 + grace_ms，force=true 可关闭终端，高风险打包/签名/安装/迁移/归档变更/批量移动需宿主本地确认，AI 不能批准
- **send_command_input**: 向运行中命令终端发文本，默认追加换行，用于交互式提示/REPL

### 任务与进度
- **set_todos**: 设置完整持久任务列表，结构 [{id, title, status: pending|in_progress|completed|...}]
- **update_plan**: 兼容 Codex plan 数组，同 set_todos 持久化
- **report_progress**: 瞬态进度汇报，不改文件，可选 todo_id

## 5. 已验证调用

```python
# 列目录
await session.call_tool("list_directory", {"path": ".", "depth": 1})
# 读文件
await session.call_tool("read_files", {"paths": ["README.md"]})
# 搜索
await session.call_tool("search_files", {"pattern": "WebView2", "context_lines": 2})
# 执行命令
await session.call_tool("run_command", {"command": "ls -la && dotnet --version"})
```

已成功列出根目录、src/assets/scripts，读取 README、说明、app.config。

## 6. 准备好的工作流

1. **发现**: `list_directory` + `find_files` 并行探测结构
2. **定位**: `search_files` + `lsp` 缩小范围
3. **读取**: `read_files` 批量读关键文件，记录 version hash
4. **计划**: `set_todos` 发布完整计划
5. **编辑**: `apply_patch` 一次提交多文件，保持模块化
6. **验证**: `get_diagnostics` + `run_command` 构建/测试
7. **同步**: 写入 `docs/mcp-<topic>.md`
8. **进度**: 关键节点 `report_progress`

## 7. 注意事项

- Tunnel URL (trycloudflare) 可能过期，需用户提供新 URL
- 远程是 Windows，路径分隔、换行 (CRLF/LF) 由 runtime 自动保留，但 docs 要求 LF
- 无认证头，靠 mcp-session-id 维持会话，DELETE 终止
- 并行优势大，建议批量：如同时 `read_files` 5个文件 + `search_files` 3个查询
- 编辑大文件时保持 hunk 聚焦，避免重写多用途块，必要时抽 helper
- 高风险操作 (打包/签名/安装) 会被宿主要求本地确认，无法自动化

## 8. 下一步待命

- 等待用户下发具体任务系列
- 可立即执行：代码审计、功能新增、bug 修复、构建验证、文档生成
- 建议先 `set_todos` 明确任务，再并行探索

---
*生成于沙箱 /home/user/docs/mcp-bridge-guide.md，同时需同步到远程 docs/*
