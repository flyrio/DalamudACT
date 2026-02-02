# how - DoT 对齐 ACT（比例/归因）与历史稳定性修复

## 1) ACT.McpPlugin：修复 `dotDamage` 的获取口径
为了让 “DoT 占比核对” 可行，`dotDamage` 的读取策略调整为更贴近 ACT UI/插件真实计算的路径：
1. 遍历 `CombatantData.ColCollection`，自动定位疑似 DoT 伤害列，并用 `GetColumnByName` 读取。
2. 回退读取 `CombatantData.Tags`（部分插件会写入 DoT 统计）。
3. 兜底从 `CombatantData.AllOut` 聚合（常见以 `"(DoT)"` 后缀标注跳伤）。
4. 最后对 `ColCollection` 中疑似字段做一次扫描兜底。
并保持对格式化字段（千分位、`k/m`、`xxx (yy%)`）的容错解析，避免不同语言/样式导致读取失败。

## 2) Dalamud：DoT tick 捕获与归因补强（更贴近 ACT）
### 2.1 Network DoT tick 放行条件增强
在 `ActionEffect sourceId==0xE0000000` 的 DoT tick 入口中：
- 维持原有 “命中 DoT 状态表/映射表” 的快速判定；
- 启用增强归因时，允许通过 **目标状态表存在该 `statusId`** 放行，覆盖 DotPot 表缺失的 DoT/地面 DoT/特殊 DoT。

### 2.2 DotEventCapture：缺字段场景的消歧补强
在增强归因开启时：
- 当 tick 同时缺失 `sourceId` 与 `buffId`，允许用目标状态候选 + tick 伤害做一次 `(sourceId,buffId)` 配对消歧（失败再回退兜底路径）。
- 当 `buffId` 后续被推断补齐但 `sourceId` 仍未知时，允许再次尝试按伤害匹配来源，减少多来源同 `statusId` 场景的未知回退。

### 2.3 默认开关策略
将配置版本升级后默认启用 `EnableEnhancedDotCapture`，使 DoT 的捕获/归因/明细在默认状态下更贴近 ACT 口径，减少“需要手动开关才生效”的调试成本。

## 3) 历史战斗稳定：统一“有效战斗”判定
新增 `ACTBattle.HasMeaningfulData()`：
- 以 **实际总伤害>0** 或 **存在未知来源 DoT/LB** 作为“有效战斗”判定；
- 战斗结束写入历史、UI 历史筛选、导出选择均统一使用该判定，避免仅建档但 0 伤害的空白战斗挤占有限历史容量。

## 4) ActDllBridge：降低 `InitPlugin` 试跑崩溃风险
- 将 `InitPlugin` 调用放到 **STA 线程** 执行（WinForms 线程模型要求）。
- 对创建的 `TabPage/Label` 进行 **显式 Dispose**，并在结束前触发一次 GC/finalizer 清理，降低 Finalizer 线程异常导致的崩溃概率。
- 在 `tryInitPlugin=true` 的试跑模式下跳过 `AssemblyLoadContext.Unload`，避免“可回收卸载 + WinForms/线程残留”导致的不可控风险。

