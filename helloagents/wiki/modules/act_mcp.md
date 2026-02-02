# act_mcp

## 目的
为 **ACT.DieMoe** 提供一个可被 MCP 客户端（本对话）调用的本地通讯桥，实现“对话启动自动连接”与基础工具能力（状态查询、写入 ACT 日志、读取插件日志）。

## 模块概述
- **职责**：
  - ACT 插件侧：启动本地 Named Pipe JSON-RPC 服务
  - MCP 服务器侧：通过 stdio 实现 MCP 2024-11-05 tools，并桥接到 Named Pipe
- **状态**：可用（支持状态/日志/遭遇统计快照）
- **最后更新**：2026-01-29

## 关键设计

### 组件划分
- `ActMcpBridge/ACT.McpPlugin`（net48）
  - `Advanced_Combat_Tracker.IActPluginV1` 插件入口
  - Named Pipe 服务端（仅允许当前用户 SID 连接；支持多客户端并发连接，并对 RPC handler 做串行化以避免并发访问 ACT 数据）
  - RPC 方法：`ping`、`act/status`、`act/notify`、`act/log/tail`
- `ActMcpBridge/ACT.McpServer`（net10.0）
  - MCP stdio server（协议：2024-11-05）
  - tools：`act_status`、`act_notify`、`act_log_tail`
  - 自动连接/断线重连到 Named Pipe

### 遭遇统计（对齐 ACT）
- `act/status` 会在基础版本信息外，附带 **当前遭遇（ActiveEncounter）快照**（如存在）：
  - `zone` / `title` / `durationS` / `startTime` / `endTime`
  - `pluginLocation`：插件 DLL 路径（优先 `Assembly.Location`，空时回退 `CodeBase` 解析），便于排查部署位置
  - `combatants`：按伤害降序的 TopN（默认 20），包含 `name` / `damage` / `dotDamage` / `encdps` / `dps`
    - 调试字段：`dotDamageMethod` / `dotDamageKey`（用于定位当前环境下 DoT 统计口径来自哪一条路径/哪一列）
- `dotDamage` 获取策略（尽量贴近 ACT UI/插件的真实计算口径）：
  1) 优先从 `CombatantData.Items`（`DamageTypeData`）聚合：先按 DamageType bucket 识别 DoT；不命中则扫描 `DamageTypeData.Items`（`AttackType`）并用 `Type/Name/Tags` 判定
  2) 回退遍历 `CombatantData.ColCollection`，自动定位疑似 DoT 伤害列，并通过 `GetColumnByName` 读取（匹配规则放宽：允许列名仅包含 `DoT/Tick/持续/周期/跳` 等关键词，不要求显式包含 “Damage/伤害”）
  3) 回退读取 `CombatantData.Tags`（部分插件会把 DoT 统计写入 Tags）
  4) 兜底从 `CombatantData.AllOut` 聚合（常见以 `"(DoT)"` 后缀标注跳伤）
  5) 最后对 `ColCollection` 中所有疑似 DoT 数值列做一次扫描兜底
  - 对格式化字段（千分位、`k/m`、`xxx (yy%)`）做容错解析，以提升不同版本/语言/列名下的兼容性
- `act_notify` 支持以 **命令消息** 触发一次性的统计输出，便于通过 `act_log_tail` 拉取：
  - `message="mcp:stats"`：输出当前遭遇概要 + TopN combatants（默认 20）
  - `message="mcp:stats top=8"`：输出指定 TopN（1-200）
  - 输出格式包含 `dot=`，便于直接从日志确认 `dotDamage` 是否已正确取到
  - `message="mcp:items name=YOU contains=dot top=40"`：输出指定 combatant 的 `Items` 键值（可用 `contains=` 过滤），用于排查“DoT 字段/列名在当前 ACT 环境下实际叫什么”
  - 日志编码：为避免中文在 ACT 日志中出现乱码，统计输出中的 `name/key/value` 会做转义（`\uXXXX`），便于在 `act_log_tail` 中稳定读取

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
