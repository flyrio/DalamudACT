# task

> 轻量迭代：默认启用 ACT MCP 同步，修复历史战斗被空白覆盖，并让导出/展示尽可能与 ACT 一致

## 任务清单

- [√] 配置：升级到 v21 后默认启用 `EnableActMcpSync=true`（`PreferActMcpTotals=true`）
- [√] 历史：`HasMeaningfulData` 不再把“仅未知来源 DoT”视为有效战斗，减少空白记录覆盖
- [√] 同步：ACT MCP 轮询在当前战斗槽位为空时将快照写入上一场战斗，避免空白槽位被旧遭遇覆盖
- [√] 导出：`act_mcp` 行导出使用 ACT 的 `combatants` 生成完整 `actors` 列表，确保 `totalDamageAll/totalDotDamage` 对齐
- [√] 性能：非战斗状态下 ACT MCP 轮询降频（10s），减少等待怪刷新时的无效开销
- [√] 构建：`dotnet build DalamudACT.sln -c Release` 通过

## 备注

- 验证建议：保持 ACT 开启，进入战斗后观察 UI 的总伤害/DPS/DOT 占比是否与 ACT 一致；并留意战斗结束后历史翻页是否仍出现“空白战斗覆盖”。可用 `/act stats file both` 采样。
