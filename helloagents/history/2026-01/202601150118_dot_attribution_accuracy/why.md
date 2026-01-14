# 变更提案: DoT 归因准确性修复

## 需求背景
多人同职业、多目标场景下，DoT tick 日志中频繁出现 `buffId=0` 或 `src=E0000000`，导致 DoT 归因与分配不稳定：主列表/汇总/Tooltip/技能明细均出现 DoT 偏低或波动。现有去重策略对未知来源 tick 可能发生误删，且 DoT 威力表存在缺失状态（如 1221、4972），影响状态列表推断与模拟分配。

## 变更内容
1. 调整 DoT tick 归因与去重策略：已知来源 tick 按实际伤害直接计入；未知来源仅用于模拟分配；去重仅跨通道生效，避免未知来源 tick 被误删。
2. 增强 buffId 推断：当 buffId 缺失时，基于目标状态列表的唯一匹配或按伤害匹配推断；无法确认时保持未知并走模拟分配。
3. 同步并补齐 DoT 威力表：通过 PotencyUpdater 从 ff14mcp 同步 DotPot，必要时添加手动覆盖，确保缺失状态纳入分配与推断。

## 影响范围
- **模块:** capture / battle / potency
- **文件:**
  - `DalamudACT/ACT2.cs`
  - `DalamudACT/DotEventCapture.cs`
  - `DalamudACT/Struct/ACTBattle.cs`
  - `DalamudACT/Potency.cs`
  - `tools/PotencyUpdater/Program.cs`
  - `helloagents/wiki/modules/battle.md`
  - `helloagents/wiki/modules/potency.md`
  - `helloagents/CHANGELOG.md`
- **API:** 无
- **数据:** DotPot/DoT 状态与威力

## 核心场景

### 需求: DoT 归因准确性
**模块:** battle / capture

#### 场景: 多人同职业+多目标
同职业多名玩家在多目标战斗中持续挂 DoT。
- 预期结果: 已知来源 tick 直接归因；未知来源仅模拟分配；主列表/汇总/Tooltip 的 DoT 统计一致。

#### 场景: buffId=0/来源未知
DoT tick 记录中 `buffId=0` 或 `src=E0000000`。
- 预期结果: 若可唯一推断 buffId 则用于模拟分配；否则不误删 tick，仍计入 TotalDotDamage。

### 需求: DoT 威力表补齐
**模块:** potency

#### 场景: 目标状态含未收录 DoT
目标状态列表包含 DotPot 未收录的 DoT 状态。
- 预期结果: DotPot 覆盖缺失状态，推断与模拟分配可正确参与计算。

## 风险评估
- **风险:** 推断错误导致归因偏差
  - **缓解:** 仅在唯一匹配或误差阈值内推断，保留未知回退路径
- **风险:** 状态列表扫描带来性能开销
  - **缓解:** 仅在 `buffId=0` 情况触发，复用缓存
- **风险:** Potency 同步来源不一致
  - **缓解:** 记录同步来源与手动覆盖项，必要时与实测对照
