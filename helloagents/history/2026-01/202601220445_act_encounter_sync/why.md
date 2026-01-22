# 变更提案: 对齐 ACT 遭遇计时与生命周期（ActiveEncounter）

## 需求背景
当前 DalamudACT 的伤害统计在“遭遇开始/结束时间”口径上与 ACT 的 `ActiveEncounter` 不一致，导致以下问题：
1. **ENCDPS 不可对齐**：同一场战斗中，插件侧 ENCDPS 与 ACT 差异明显（主要由 duration 不一致引起）。
2. **中途加入/脱战波动**：当本地未进入 `InCombat` 或在多人连续战斗场景中 `InCombat` 闪断时，插件会提前结束/拆分战斗段，而 ACT 仍维持同一 `ActiveEncounter`。
3. **对齐调试成本高**：需要频繁通过命令导出统计快照，难以在真实战斗中稳定抓取对齐样本。

## 变更内容
1. 将遭遇生命周期从“本地 `InCombat`”切换为“全局战斗事件 + 失活超时（与 ACT 一致）”。
2. 遭遇计时以“出现伤害类事件”为准，允许由怪物先手/他人先手启动遭遇（对齐 ACT 起始）。
3. 召唤物 Owner 解析优先使用对象表实时值，避免缓存/EntityId 复用导致的错归因。
4. 增加“战斗结束自动导出 BattleStats(JSONL)”开关，便于 MCP/脚本与 ACT 对齐。

## 影响范围
- **模块:**
  - `DalamudACT`（战斗采集、遭遇计时、归因/缓存、导出能力）
- **文件:**
  - `DalamudACT/ACT2.cs`
  - `DalamudACT/Struct/ACTBattle.cs`
  - `DalamudACT/Configuration.cs`
  - `DalamudACT/PluginUI.cs`

## 核心场景

### 需求: ENCDPS 与 ACT 对齐
**模块:** battle

#### 场景: 怪物先手后我方出手
- 插件遭遇开始时间不晚于 ACT（允许由入站伤害启动）
- 插件 ENCDPS 与 ACT 在相同采样时刻保持一致（误差仅来自采样时刻差）

#### 场景: 多人连续战斗（本地 InCombat 闪断）
- 插件不因本地 `InCombat` 短暂变化提前结束遭遇
- 遭遇结束由“最后战斗事件 + 超时”决定，与 ACT 行为一致

## 风险评估
- **风险:** 连续战斗场景下遭遇可能更“长”，显示/统计会更接近 ACT，但可能与旧版“仅本地战斗段”的习惯不一致。
- **缓解:** 保留导出与 UI 开关；并将逻辑以 ACT 为 SSOT（本需求目标）。

