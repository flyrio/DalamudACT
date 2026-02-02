## 目标
- 以 ACT 的 DoT 口径为准，补齐/修正 `dotDamage` 获取与导出对齐能力，便于在 Dalamud 侧稳定核对 DOT 占比与数值差异。

## 任务清单（轻量迭代）
- [√] 1. 修正 ACT MCP `dotDamage` 提取：增加 `CombatantData.Items` 聚合与 `AttackType` 扫描，放宽列名识别，并补充 `dotDamageMethod/dotDamageKey` 便于定位命中路径
- [√] 2. BattleStats 导出：`act_mcp` 行补齐 `totalDotDamage`（对 `actors` 的 `actDotDamage` 求和），便于快速对照 DOT 占比
- [√] 3. 本地 DoT tick 捕获：补齐 Network ActionEffect 的“非 0xE0000000 来源 DoT tick”识别，避免周期伤害被算入 `baseDamage` 导致 DoT 偏低
- [√] 4. 知识库同步：更新 `CHANGELOG.md` 与模块文档

## 验证
- [√] 代码构建通过（Release）
- [?] 运行验证：需重启/重载 ACT 以加载最新 `ACT.McpPlugin.dll`，并在战斗中用 `battle-stats.jsonl` 对照 ACT `dotDamage`/DOT 占比

