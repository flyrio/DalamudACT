# 变更提案: ACT MCP 通讯桥

## 需求背景
需要为 **ACT.DieMoe** 增加一个可被本对话（MCP 客户端）调用的通讯入口，使对话在启动时即可自动连接到 ACT，并完成基础的双向信息交互（查询状态、输出日志、发送提示）。

## 变更内容
1. 新增 **ACT 插件**：随 ACT 启动自动开启本地 IPC（Named Pipe）RPC 服务。
2. 新增 **MCP 服务器进程**：采用 MCP 2024-11-05 的 stdio 传输，向对话暴露 tools，并桥接到 ACT 插件的 IPC。
3. 自动连接：MCP 服务器在对话启动时由客户端拉起，并自动连接/重连到 ACT 插件。

## 影响范围
- **模块:** act_mcp（新增）
- **文件:**
  - `ActMcpBridge/ACT.McpPlugin/*`
  - `ActMcpBridge/ACT.McpServer/*`
  - `helloagents/wiki/modules/act_mcp.md`
  - `helloagents/wiki/api.md`
  - `helloagents/wiki/arch.md`
  - `helloagents/wiki/overview.md`
  - `helloagents/CHANGELOG.md`
- **API:** Named Pipe RPC（插件侧） + MCP tools（对话侧）
- **数据:** 无持久化数据（仅内存日志缓冲）

## 核心场景

### 需求: 对话可连接并调用 ACT
**模块:** act_mcp  
对话启动后可自动建立到 ACT 的连接，并可调用工具进行状态查询与信息输出。

#### 场景: 对话启动时自动连接
当 MCP 客户端拉起 MCP 服务器进程后：
- MCP 服务器会自动连接到 ACT 插件的 Named Pipe（失败则按退避重试）
- 连接成功后可响应 `tools/list` 与 `tools/call`

#### 场景: 查询 ACT/插件状态
对话调用 `act_status` 后：
- 返回 ACT 版本、插件版本、pipe 名称、连接状态等信息

#### 场景: 发送提示到 ACT
对话调用 `act_notify` 后：
- ACT 主界面日志区输出一行提示（带统一前缀）

## 风险评估
- **风险:** IPC 被本机其他进程滥用  
  **缓解:** Named Pipe 限制为当前用户 SID；输入长度与参数校验；仅提供低风险工具（无文件/网络/破坏性操作）。
- **风险:** 插件异常影响 ACT 稳定性  
  **缓解:** 所有 IPC 线程独立；关键入口 try/catch；停止时可安全释放资源。

