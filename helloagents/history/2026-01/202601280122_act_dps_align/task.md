# 任务清单: ACT 口径对齐（DPS/伤害）

## 1. ACT MCP Pipe 并发
- [√] ACT.McpPlugin Named Pipe 支持多客户端并发连接（避免 MCP Server 长连接独占 Pipe）
- [√] RPC handler 串行化，避免并发访问 ACT 数据导致不稳定

## 2. Dalamud 插件对齐
- [√] 启用 `PreferActMcpTotals` 时，UI 秒伤直接使用 ACT 返回的 ENCDPS/DPS（与 `DpsTimeMode` 联动）
- [√] 将 Dalamud 侧 ActMcp 连接超时调整为 800ms（降低偶发连接失败）

## 3. 知识库同步
- [√] 更新 `helloagents/wiki/modules/battle.md`
- [√] 更新 `helloagents/wiki/modules/act_mcp.md`
- [√] 更新 `helloagents/wiki/modules/ui.md`
- [√] 更新 `helloagents/CHANGELOG.md`

## 4. 验证
- [√] 本地构建：`DalamudACT.sln`（Release）
- [√] 本地构建：`ACT.McpPlugin.csproj`（Release）
- [?] 运行中验证：ACT 侧更新插件后确认 Dalamud 侧可拉取遭遇快照（EnableActMcpSync 生效）
  > 备注: 需要在 ACT 中重新加载插件或重启 ACT；当前对话无法直接热更 ACT 插件进程。