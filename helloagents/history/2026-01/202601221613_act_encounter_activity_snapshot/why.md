# 变更提案: 对齐 ACT 遭遇计时与 BattleStats 快照稳定化

## 需求背景
当前插件的本地统计在与 ACT 对照时，存在两类影响对齐质量的问题：
1. **遭遇分段不一致**：当一段时间内只有敌方动作（或来源不可归因的伤害回调）时，本地遭遇可能因 `LastEventTime` 未更新而提前结束，导致与 ACT `ActiveEncounter` 的开始/结束时刻不一致。
2. **对照采样不稳定**：战斗中执行 `/act stats both file` 进行对照时，可能因并发写入/快照不一致、或 ACT 快照非“本次即时拉取”而产生难以复现的偏差，影响定位剩余差异的效率。

## 变更内容
1. 遭遇计时：在 `ActionEffect` 检测到 damage-like effect 时推进遭遇计时（仅更新遭遇生命周期，不写入玩家伤害）。
2. BattleStats 导出：导出逻辑改为读取“战斗快照”计算，避免并发读写导致的快照不一致。
3. ACT 对照采样：
   - `both` 模式仅在“本次即时拉取到 fresh 的 ACT 快照”时输出 `source=act_mcp` 记录，避免缓存快照造成误配。
   - `file` 模式在缺少 ACT 快照时写入一条 JSON 诊断行，保证 `battle-stats.jsonl` 可持续解析。

## 影响范围
- **模块**：battle（遭遇生命周期/导出）、ui（仅行为说明）、act_mcp_bridge（对照环境）
- **文件**：
  - `DalamudACT/ACT2.cs`
  - `helloagents/wiki/modules/battle.md`
  - `helloagents/CHANGELOG.md`
  - `helloagents/history/index.md`

## 核心场景
### 需求: 对齐 ACT 遭遇分段
**模块:** battle

#### 场景: 只剩敌方动作
- 条件：玩家暂时停止输出，但敌方仍在造成伤害/触发伤害回调
- 预期结果：本地遭遇不应提前结束，应与 ACT `ActiveEncounter` 的计时更一致

#### 场景: 来源不可归因的伤害回调
- 条件：出现 `sourceId` 无法归因（或非玩家来源）的 damage-like effect
- 预期结果：本地仅推进遭遇计时，不写入玩家伤害，避免分段偏差扩大

### 需求: 稳定对照采样
**模块:** battle

#### 场景: 战斗中执行 /act stats both file
- 条件：战斗进行中，存在并发事件写入
- 预期结果：导出快照一致、文件为可持续解析的 JSONL；ACT 对照为 fresh 快照，避免误配

## 风险评估
- **风险:** `ActionEffect` 每次回调增加一次 damage-like 扫描，可能带来轻微开销
- **缓解:** 扫描在命中 damage-like effect 后立即短路；仅推进计时，不引入额外数据结构写入
