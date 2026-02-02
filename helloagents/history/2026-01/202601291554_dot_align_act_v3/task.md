# task

> 轻量迭代：对齐 ACT 的 DoT 归因与展示（v3）

## 任务清单

- [√] DoT 推断：`buffId=0` 时不再覆盖可信 `sourceId`（仅在 `sourceId` 缺失时使用配对消歧）
- [√] DoT 推断候选：不再限制“仅本地/队伍”，扩展到目标状态表中的全部玩家来源
- [√] UI：启用 `PreferActMcpTotals` 且 ACT 快照可用时，展示 ACT 侧总 DOT 伤害与参与人数
- [√] ACT MCP Bridge：日志输出使用转义（LogSafe），避免中文在 ACT 日志中乱码；并补充 `pluginLocation` 回退（CodeBase）
- [√] 构建并热重载 Dalamud 插件，导出 `battle-stats.jsonl`/`dot-debug.log` 进行对照采样

## 备注

- 对照采样建议：在战斗中执行 `/act stats file both`，并在 DoT 异常时执行 `/act dotdump file all 200`。
