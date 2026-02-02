# task - DoT 与历史战斗稳定性修复

- [√] 在 Dalamud 侧加入 ACT 遭遇快照陈旧过滤，避免同步旧计时导致空白战斗 churn
- [√] 遭遇结束时丢弃“无任何统计数据”的空白遭遇，避免挤占历史
- [√] UI 历史筛选排除无数据战斗，避免历史翻页出现空白
- [√] DoT 来源推断移除 DotSourceCache 回退，降低多来源同 statusId 时错归因风险
- [√] DotDump 增加 `DROP=RejectedSourceNotOnTarget` 标记，提升可观测性
- [√] ACT.McpPlugin 增强 dotDamage 字段探测（字段 + Items key + 扫描兜底）并在 `mcp:stats` 日志输出 dot
- [√] 本地构建：`DalamudACT` Release（生成 `latest.zip`）
- [√] 本地构建：`ACT.McpPlugin` Release（更新 `bin/Release/net48/ACT.McpPlugin.dll`）

