# task - DoT 对齐 ACT（比例/归因）与历史稳定性修复

- [√] ACT.McpPlugin：`dotDamage` 改为 ColCollection/GetColumnByName 优先，回退 Tags/AllOut 聚合与扫描兜底
- [√] Dalamud：Network DoT tick 放行补强（启用增强归因时允许通过目标 StatusList 判定）
- [√] DotEventCapture：增强归因增加 `(sourceId,buffId)` 配对消歧与二次来源按伤害匹配
- [√] 配置：版本升级默认启用 `EnableEnhancedDotCapture`
- [√] 历史稳定：新增 `ACTBattle.HasMeaningfulData()` 并用于战斗结束/历史筛选/导出选择
- [√] actdll init：改为 STA 线程执行并显式 Dispose 控件；试跑模式跳过 ALC.Unload 以降低崩溃风险
- [√] 知识库：更新 `wiki/modules/act_mcp.md`、`wiki/modules/battle.md`、`CHANGELOG.md`
- [√] 构建：`DalamudACT` Release（生成 `latest.zip`）
- [√] 构建：`ACT.McpPlugin` Release（更新 `bin/Release/net48/ACT.McpPlugin.dll`）

