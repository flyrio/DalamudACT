# act_mcp

## 目的
为 **ACT.DieMoe** 提供一个可被 MCP 客户端（本对话）调用的本地通讯桥，实现“对话启动自动连接”与基础工具能力（状态查询、写入 ACT 日志、读取插件日志）。

## 模块概述
- **职责**：
  - ACT 插件侧：启动本地 Named Pipe JSON-RPC 服务
  - MCP 服务器侧：通过 stdio 实现 MCP 2024-11-05 tools，并桥接到 Named Pipe
- **状态**：可用（支持状态/日志/遭遇统计快照）
- **最后更新**：2026-01-22

## 关键设计

### 组件划分
- `ActMcpBridge/ACT.McpPlugin`（net48）
  - `Advanced_Combat_Tracker.IActPluginV1` 插件入口
  - Named Pipe 服务端（仅允许当前用户 SID 连接）
  - RPC 方法：`ping`、`act/status`、`act/notify`、`act/log/tail`
- `ActMcpBridge/ACT.McpServer`（net10.0）
  - MCP stdio server（协议：2024-11-05）
  - tools：`act_status`、`act_notify`、`act_log_tail`
  - 自动连接/断线重连到 Named Pipe

### 遭遇统计（对齐 ACT）
- `act/status` 会在基础版本信息外，附带 **当前遭遇（ActiveEncounter）快照**（如存在）：
  - `zone` / `title` / `durationS` / `startTime` / `endTime`
  - `combatants`：按伤害降序的 TopN（默认 20），包含 `name` / `damage` / `encdps` / `dps`
- `act_notify` 支持以 **命令消息** 触发一次性的统计输出，便于通过 `act_log_tail` 拉取：
  - `message="mcp:stats"`：输出当前遭遇概要 + TopN combatants（默认 20）
  - `message="mcp:stats top=8"`：输出指定 TopN（1-200）

### 传输与协议
- **MCP 传输**：stdio（消息单行 JSON-RPC 2.0，按换行分隔）
- **IPC 传输**：Windows Named Pipe（消息单行 JSON-RPC 2.0，按换行分隔）

### 配置
- 环境变量（运行时）：
  - `ACT_MCP_PIPE`：Pipe 名称（默认 `act-diemoe-mcp`）
  - `ACT_MCP_CONNECT_TIMEOUT_MS`：MCP 服务器连接 Pipe 超时（默认 800ms）
- MSBuild（构建时）：
  - `ActDieMoeRoot`：ACT.DieMoe 安装目录（默认 `C:\Program Files (x86)\宝宝轮椅\ACT.DieMoe`）

## 安全
- Pipe 使用 ACL 仅允许当前用户 SID 访问。
- 工具能力仅提供低风险操作（日志/状态），不涉及文件写入、外部网络、破坏性行为。
