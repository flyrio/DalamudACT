# 任务清单: ACT MCP 通讯桥

目录: `helloagents/plan/202601220232_act_mcp_bridge/`

---

## 1. ACT 插件（IPC 服务）
- [√] 1.1 新建 `ActMcpBridge/ACT.McpPlugin`（net48）并引用 ACT 的 `Advanced Combat Tracker.dll`，实现 `IActPluginV1`，验证 why.md#需求-对话可连接并调用-act-场景-对话启动时自动连接
- [√] 1.2 实现 Named Pipe JSON-RPC 服务（ACL 限制当前用户），验证 why.md#需求-对话可连接并调用-act-场景-查询-act插件状态
- [√] 1.3 实现 `act/notify` 与插件日志缓冲，验证 why.md#需求-对话可连接并调用-act-场景-发送提示到-act

## 2. MCP 服务器（stdio tools）
- [√] 2.1 新建 `ActMcpBridge/ACT.McpServer`，实现 MCP 2024-11-05（`initialize`/`tools/list`/`tools/call`/`ping`），验证 why.md#需求-对话可连接并调用-act-场景-对话启动时自动连接
- [√] 2.2 实现 Named Pipe 客户端与自动重连，验证 why.md#需求-对话可连接并调用-act-场景-查询-act插件状态

## 3. 安全检查
- [√] 3.1 执行安全检查（输入校验、pipe 访问控制、避免对 stdout 输出非协议内容）

## 4. 文档更新
- [√] 4.1 更新 `helloagents/wiki/modules/act_mcp.md`、`helloagents/wiki/api.md`、`helloagents/wiki/arch.md`、`helloagents/wiki/overview.md`
- [√] 4.2 更新 `helloagents/CHANGELOG.md`

## 5. 构建验证
- [√] 5.1 `dotnet build` 验证两个项目可编译
