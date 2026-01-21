# 技术设计: ACT MCP 通讯桥

## 技术方案

### 核心技术
- **ACT 插件:** C# / .NET Framework 4.8（实现 `Advanced_Combat_Tracker.IActPluginV1`）
- **IPC:** Windows Named Pipe，消息为单行 JSON（JSON-RPC 2.0，newline 分隔）
- **MCP 服务器:** C# / .NET（stdio 传输），实现 MCP 2024-11-05（tools）

### 实现要点
- ACT 插件在 `InitPlugin` 阶段启动 Named Pipe 服务并写入状态到插件面板与 ACT 日志。
- Named Pipe 使用 ACL 仅允许当前用户连接（降低本机滥用风险）。
- MCP 服务器实现 `initialize`/`notifications/initialized`/`tools/list`/`tools/call`/`ping`，并将工具调用映射到 Named Pipe RPC。
- MCP 服务器具备自动连接/断线重连能力。

## 架构设计
```mermaid
flowchart LR
    A[MCP Client<br/>本对话] -->|stdio JSON-RPC| B[ACT.McpServer]
    B -->|Named Pipe JSON-RPC| C[ACT.McpPlugin]
    C --> D[ACT.DieMoe]
```

## 架构决策 ADR

### ADR-001: 采用“外部 MCP 进程 + 插件 IPC”架构
**上下文:** ACT 插件运行在宿主进程内，不适合作为 stdio 子进程承载 MCP 生命周期；同时需要满足“对话启动自动连接”的体验。  
**决策:** 由 MCP 客户端拉起独立的 `ACT.McpServer`（stdio），再通过 Named Pipe 与 ACT 插件通讯。  
**理由:** 易于自动启动/重连；对 ACT 风险隔离；实现成本低；便于未来扩展 tools。  
**替代方案:** 在 ACT 插件内直接实现 HTTP(SSE) MCP → 拒绝原因: 需要额外端口与鉴权/Origin 防护，且与客户端启动模型不如 stdio 直接。  
**影响:** 需要同时部署 ACT 插件与 MCP 服务器；两者版本需保持兼容（通过 `act/status` 协商）。

## API 设计

### 插件侧 IPC（Named Pipe JSON-RPC）
- **传输:** 单行 JSON，按 `\\n` 分隔；每条消息不得包含真实换行
- **请求/响应:** JSON-RPC 2.0（`id` 为 string/number）
- **方法:**
  - `ping` → `{}`  
  - `act/status` → 版本/连接状态
  - `act/notify` → 写入 ACT InfoLog
  - `act/log/tail` → 返回插件内存日志缓冲

### 对话侧 MCP tools（stdio）
- `act_status`：读取 `act/status`
- `act_notify`：调用 `act/notify`
- `act_log_tail`：调用 `act/log/tail`

## 安全与性能
- **安全:** Pipe 限制当前用户 SID；参数长度限制；拒绝未知方法；不实现破坏性操作。
- **性能:** 仅文本消息；串行处理；后台线程不阻塞 ACT UI。

## 测试与部署
- **构建:** `ACT.McpPlugin`（net48）与 `ACT.McpServer`（net10.0/可发布为单文件）
- **部署:**
  - ACT 插件 DLL 由 ACT 插件管理器加载（或放入 Plugins 目录后添加）
  - MCP 客户端配置为对话启动时自动拉起 `ACT.McpServer`

