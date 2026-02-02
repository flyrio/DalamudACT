# how - DoT 与历史战斗稳定性修复

## 1. 空白战斗覆盖历史：根因与修复

### 根因推断
- 当启用 ACT MCP 同步时，本地战斗会同步 `ActEncounter` 的 `Start/Last/End` 计时。
- 若 ACT 返回的是 **陈旧（已结束很久）** 的遭遇快照，本地会立刻满足“失活超时”条件，进而频繁创建新战斗槽位。
- 这些战斗往往没有本地 `DataDic`，因此在 UI/历史中表现为空白，并会不断挤占有限的历史容量。

### 修复策略
- **陈旧遭遇过滤**：以 `now - ActEncounter.EndTimeMs` 判定快照是否陈旧；超过 `EncounterTimeoutMs + Grace` 则忽略并清除快照，避免反复同步旧计时。
- **空白遭遇丢弃**：当遭遇结束时若没有任何统计数据（无玩家伤害/无 DoT/无 LB），直接重置当前槽位而不是写入历史。
- **UI 历史筛选收紧**：历史浏览只展示“确实有数据”的战斗，排除仅有计时但无数据的时间片。

## 2. DoT 归因异常：修复要点

### 主要风险点
- 当 `sourceId` 缺失但 `buffId` 已知时，使用 `(targetId,buffId)` 的单值缓存回退会在“同一 statusId 多来源并存”时产生 **错归因**（把多个来源的 tick 归到最后一次缓存的来源）。

### 修复策略
- **移除 DotSourceCache 回退**：`TryResolveDotSource` 与 `TryResolveDotSourceByDamage` 失败时不再回退到缓存来源，改为走未知来源口径，避免把 tick 错算到某个玩家。
- **补充可观测性**：当触发“目标无该来源 DoT → 回退未知来源”时，在 `DotDump` 中以 `DROP=RejectedSourceNotOnTarget` 明确标记，便于定位与统计对照。

## 3. ACT DoT 指标抓取（用于比例对齐）

### 问题
ACT MCP 原先对 `dotDamage` 的字段探测不足，导致输出长期为 0，无法用于“DoT 占比”核对。

### 修复策略
- `ACT.McpPlugin` 在构建 combatant 快照时：
  1) 尝试常见公开字段（`DoTDamage`/`DotDamage` 等）  
  2) 尝试常见 Items key（如 `Damage (DoTs)` 等变体）  
  3) 兜底扫描 `CombatantData.Items`，选择最像 DoT 伤害的指标（key 含 `dot` 且含 `damage/dmg`，排除 `dps/%`）
- `mcp:stats` 日志输出补充 `dot=`，便于用 `act_log_tail` 直接检查是否生效。

