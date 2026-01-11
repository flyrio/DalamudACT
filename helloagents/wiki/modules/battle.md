# battle

## 目的
聚合伤害事件并输出战斗统计口径（总伤害、DPS/ENCDPS、DoT 分配等）。

## 模块概述
- **职责**：维护 `ACTBattle`（遭遇）内的伤害/死亡/DoT 统计与持续时间口径
- **状态**：稳定
- **最后更新**：2026-01-12

## 关键设计

### DPS 口径（对齐 ACT）
- **ENCDPS**：`Damage / EncounterDuration`（战斗时长）
- **DPS**：`Damage / CombatantDuration`（个人活跃时长：该玩家首末次造成伤害的时间窗）
- **EncounterDuration**：`StartTime`（首个事件）到 `EndTime`（末个事件）的事件驱动毫秒时间，避免帧级计时偏差

### 事件计入门槛（避免丢事件）
- 不再依赖 `LocalPlayer.StatusFlags.InCombat` 作为硬门槛
- 计入条件：**战斗已开始**（`StartTime != 0`）或 **`ConditionFlag.InCombat` 为真**，或 **来源为本地/小队成员**
- 目的：避免死亡/脱战边界、对象表缺失等导致的伤害漏算

### DoT 处理（未知来源对齐）
- **已知来源 DoT**：按来源计入该玩家总伤害（并记录 `DotDamageByActor`）
- **未知来源 DoT**：
  - 始终计入 `TotalDotDamage`（即使无法读取目标状态列表）
  - 若可读取目标状态：结合 `(targetId,buffId)->sourceId` 缓存与目标身上同 `buffId` 的 `SourceId` 推断/分配，并通过 `CalcDot` 更新 `DotDmgList`
  - 若不可读取目标状态：仍触发 `CalcDot` 以保持 `TotalDotDamage` 与分配结果一致，避免总伤害偏低

### Limit Break（LB）
- LB 伤害计入施放者总伤害，同时保留 LB 分项统计（`LimitBreak`）

## 关联模块
- core
- potency
- ui

## 历史记录
- [202601092006_dot_damage_accuracy](../../history/2026-01/202601092006_dot_damage_accuracy/) - DoT 统计口径修正
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 对齐 ACT：DPS/ENCDPS、LB、ActionEffect、多目标、DoT 归因
- [202601120627_act_damage_sync_v2](../../history/2026-01/202601120627_act_damage_sync_v2/) - 对齐 ACT：修复丢事件/玩家建档/未知 DoT 漏算

