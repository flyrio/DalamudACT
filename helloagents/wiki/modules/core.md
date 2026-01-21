# core

## 目的
维护插件核心 Hook/解析链路，将游戏内事件转换为 `ACTBattle` 可统计的数据结构，并确保与 ACT 口径一致。

## 模块概述
- **职责**：Hook 回调、ActionEffect 解析、DoT/死亡事件接入、战斗起止与缓存管理
- **状态**：稳定
- **最后更新**：2026-01-21

## 关键设计

### ActionEffect 解析（伤害事件）
- 入口：`ActionEffectHandler.Receive` Hook（签名：`ActionEffectHandler.Addresses.Receive.String`）
- `targetCount`：使用 `Header.NumTargets`，按真实目标数遍历（不因 `targetId==0` 提前 break）
- 每目标解析 8 个 `Effect`，仅统计伤害类 `Type`（Damage/BlockedDamage/ParriedDamage）
- 技能 ID：优先使用 `Header.SpellId`，为 0 时回退到 `Header.ActionId`
- 伤害值：`Value | (Param3 << 16)`（对伤害类 effect 直接合并高位，避免依赖 `Param4` 标记位导致 >65535 截断）

### DoT/死亡事件
- DoT：由 `ActorControlSelf`（`ActorControlCategory.DoT`）接入
  - 来源推断：优先扫描目标 `StatusList`（唯一匹配/按伤害匹配），失败再回退 `(targetId,buffId)->sourceId` 缓存；启用 `EnableActLikeAttribution` 时不使用缓存以减少错归因
  - 目标定位使用 `ObjectTable.SearchByEntityId(targetId)`（DoT 链路传递的是 `EntityId`，避免误用 `SearchById` 导致无法读取 `StatusList`）
  - 仅接受有效的 DoT 目标（`targetId/entityId` 必须是 `BattleNpc EntityId` 且不为 `0xE0000000`），避免无效包污染统计
  - DoT 去重覆盖：
    - 同通道完全重复（窗口 200ms），降低重复回调/重复包导致的 DoT 偏高
    - `buffId=0` 与 `buffId!=0` 的同通道双事件重复（窗口 800ms，判重使用原始 `buffId`，不受推断补齐影响），避免同一 tick 被统计两次
    - 跨通道重复（窗口 800ms）：不同通道同 tick 上报时去重；当两侧 `buffId`（原始或推断）均可确定且明确不一致时跳过去重，避免多 DoT 场景误删
  - 无法解析来源时，仍计入 `TotalDotDamage`，并更新 `CalcDot`，避免总伤害偏低
  - 调试：`/act dotstats`（统计汇总）与 `/act dotdump`（最近 tick 明细）可用于与卫月/ACT 做对照
- Death：由 `ActorControlSelf`（`ActorControlCategory.Death`）接入，计入 `Data.Death`

### 玩家建档（避免对象表缺失丢事件）
- 统一通过 `ACTBattle.EnsurePlayer` 建档：
  - 优先从 `ObjectTable` 解析玩家名称/职业
  - 失败时从 `PartyList` 兜底解析（支持队友不在对象表范围时仍可统计）
  - 即使无法解析名称/职业，也会先创建占位统计条目，确保伤害不丢失

### 战斗起止与计时
- 战斗时间以事件驱动毫秒计时：
  - `StartTime`：首次计入事件时间
  - `EndTime`：战斗中持续更新；脱战时冻结到 `LastEventTime`
- 脱战清理：清空 `ActiveDots`、清理宠物 Owner 缓存（`OwnerCache`）

## 关联模块
- battle
- potency
- ui

## 历史记录
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 对齐 ACT：多目标/DoT/计时口径
- [202601120627_act_damage_sync_v2](../../history/2026-01/202601120627_act_damage_sync_v2/) - 修复丢事件/PartyList 兜底/未知 DoT 漏算
- [202601121134_damage_parser_ref_deathbufftracker](../../history/2026-01/202601121134_damage_parser_ref_deathbufftracker/) - 参考 DeathBuffTracker：切换 ActionEffectHandler 解析与签名，修正目标遍历
