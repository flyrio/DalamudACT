# 任务清单: 遭遇不误分段（InCombat keepalive）+ ACT MCP Pipe 修复

## 1. 遭遇不误分段
- [√] 当本地仍处于 `InCombat` 时，不因短暂无事件（如 boss 转阶段上天/无敌）而结束遭遇并重置战斗记录
- [√] 增加 `InCombat` keepalive（约 1s 推进一次 `LastEventTime`），使计时/DPS 在无事件阶段仍可连续

## 2. ACT MCP Pipe 修复
- [√] 修复 ACT.McpPlugin Pipe 服务器多客户端实现中的变量捕获问题，避免客户端连接后无响应

## 3. 知识库同步
- [√] 更新 `helloagents/wiki/modules/battle.md`
- [√] 更新 `helloagents/CHANGELOG.md`
- [√] 更新 `helloagents/history/index.md`

## 4. 验证
- [√] 本地构建：`DalamudACT.sln`（Release）
- [√] 本地构建：`ACT.McpPlugin.csproj`（Release）
- [?] 运行中验证：重载/重启 ACT 后确认 `act_status`/Dalamud 侧同步恢复，且转阶段不再误分段
  > 备注: 需要在 ACT 中重新加载插件或重启 ACT。