# battle

## 目的
聚合伤害事件并输出战斗统计口径（总伤害、DPS/ENCDPS、DoT 分配等）。

## 模块概述
- **职责**：维护 `ACTBattle`（遭遇）内的伤害/死亡/DoT 统计与持续时间口径
- **状态**：稳定
- **最后更新**：2026-01-21

## 关键设计

### DPS 口径（对齐 ACT）
- **ENCDPS**：`Damage / EncounterDuration`（战斗时长）
- **DPS**：`Damage / CombatantDuration`（个人活跃时长：该玩家首末次造成伤害的时间窗）
- **EncounterDuration**：`StartTime`（首个事件）到 `EndTime`（末个事件）的事件驱动毫秒时间，避免帧级计时偏差

### 事件计入门槛（避免丢事件）
- 不再依赖 `LocalPlayer.StatusFlags.InCombat` 作为硬门槛
- 计入条件：**战斗已开始**（`StartTime != 0`）或 **`ConditionFlag.InCombat` 为真**，或 **来源为本地/小队成员**
- 目的：避免死亡/脱战边界、对象表缺失等导致的伤害漏算

### DoT 处理（tick 采集与归因）
- **入口**：`ACT2.ReceiveActorControlSelf` 捕获 DoT tick → `DotEventCapture` 统一排队与去重 → `ACTBattle` 写入统计
- **补充入口（Network）**：当 `ActionEffectHandler.Receive` 的 `sourceId==0xE0000000` 且 `actionId` 命中 DoT 状态表时，视为“未知来源 DoT tick”入队（`DotTickChannel.NetworkActorControlSelf`），用于覆盖 ActorControlSelf 不完整/不可见的场景，并依赖 `DotEventCapture` 的跨通道去重避免翻倍
- **已知来源 DoT**：
  - 总伤害：计入来源玩家总伤害（`AddEvent`/`AddDotDamage`），并刷新个人活跃时长
  - Tick 统计：累计到 `DotDamageByActor` / `DotTickCountByActor`（Tooltip 的 Tick 部分）
  - 技能明细（可选）：当启用 `EnableEnhancedDotCapture` 且可确定 `buffId` 时，按 `statusId` 计入技能明细，便于查看 DoT 每 tick 变化
- **buffId=0 修复（唯一匹配）**：当 tick 报文缺失 `buffId` 且来源已知时，尝试在目标 `StatusList` 中按 `(targetId, sourceId)` 唯一匹配一个 DoT 状态（`TryResolveDotBuff`）；多 DoT 同源则不推断，避免误归因
- **buffId=0 修复（按伤害匹配）**：当来源已知但同源存在多个 DoT（如诗人双 DoT）导致唯一匹配失败时，使用 tick 实际伤害 + 该玩家 DPP + DoT 威力做保守选择（`TryResolveDotBuffByDamage`），仅在差异足够大时才归因
- **DoT 威力来源**：DoT 威力表（`Potency.DotPot`）可通过 PotencyUpdater 的 `--ff14mcp-dots` 从 ff14mcp 的 `dots_by_job.json` 同步，减少版本漂移与手工维护
- **未知来源 DoT**：
  - 始终计入 `TotalDotDamage`（即使无法读取目标状态列表）
  - 若可读取目标状态：结合 `(targetId,buffId)->sourceId` 缓存与目标身上同 `buffId` 的 `SourceId` 推断/分配，并通过 `CalcDot` 更新 `DotDmgList`
  - 若不可读取目标状态：仍触发 `CalcDot` 以保持 `TotalDotDamage` 与分配结果一致，避免总伤害偏低
- **未知来源 DoT（按伤害匹配）**：当 tick 同时缺失 `sourceId` 与 `buffId` 时，启用增强归因后仅推断 `buffId`（优先唯一状态，其次按伤害匹配），不按伤害强行推断来源；若推断出 `buffId` 后目标状态表能唯一给出来源，则补齐 `sourceId`，否则回退到模拟分配
- **去重口径**：
  - 同通道完全重复：过滤重复回调/重复包（窗口 200ms）
  - 跨通道重复：Legacy/Network 双通道重复时丢弃（窗口 800ms）；当两侧 `buffId`（原始或推断）均可确定且明确不一致时跳过去重，避免不同 DoT 同伤害误删
  - 同通道双事件：当同一 tick 同时出现 `buffId=0` 与 `buffId!=0` 两条事件时视为重复（窗口 800ms，判重使用原始 `buffId`，不受推断补齐影响），避免 DoT 翻倍；同毫秒内优先保留带 `buffId` 的事件
- **模拟分配兜底（DPP 不就绪）**：`CalcDot` 会用已知玩家 DPP 的平均值作为 fallback，让分配更连续，避免未记录基准技能导致 DoT 长时间不变动
- **诊断口径**：
  - `EnableDotDiagnostics` 开启后在设置窗口展示入队/处理/去重/未知来源/推断次数，并输出 Verbose 日志
  - 命令 `/act dotstats [log|file]`：输出采集统计 + 自己 DoT 的 Tick/模拟/合计（便于与 ACT 的 Simulated DoTs 对照）
    - 默认输出到聊天栏；`log` 写入 `dalamud.log`（Info 级）；`file` 导出到 `pluginConfigs/DalamudACT/dot-debug.log`
  - 命令 `/act dotdump [log|file] [all] [N]`：输出最近 DoT tick 事件（含去重/归因），用于定位错归因/重复统计
    - 默认输出到聊天栏；`log` 写入 `dalamud.log`；`file` 导出到 `pluginConfigs/DalamudACT/dot-debug.log`
  - 设置窗口 → DoT：提供按钮一键写入日志/导出文件（不占用聊天栏）
  - 命令 `/act stats [log|file] [N]`：导出当前战斗统计快照（JSON），用于与 ACT 对照
    - `file`：写入 `pluginConfigs/DalamudACT/battle-stats.jsonl`（推荐，便于 MCP/脚本读取）
    - `log`：写入 `dalamud.log`（单行 JSON）

### 备选模式：ACTLike 归因（DoT/召唤物）
- **开关**：配置 `EnableActLikeAttribution`（设置窗口 → DoT）
- **召唤物合并**：周期性预热 `OwnerCache`，当 `ResolveOwner` 失败时触发预热并重试，减少“召唤物伤害被丢弃”导致的漏算
- **DoT 来源推断**：当 tick 缺失 `sourceId` 且 `buffId!=0` 时，优先用目标 `StatusList` 唯一匹配来源；若同 `statusId` 多来源并存，则使用 tick 实际伤害 + 各来源 DPP + DoT 威力表做保守匹配，仅在误差与分离度满足阈值时才归因
- **兜底**：仍无法唯一归因时回退到“未知来源 DoT”口径（计入 `TotalDotDamage` 并按目标状态模拟分配），避免误归因扩大
- **建议**：复杂场景下开启 `EnableDotDiagnostics` 对比 ACT 做 A/B 验证，优先以“误归因减少/漏算减少”为判断标准

### Limit Break（LB）
- LB 伤害计入施放者总伤害，同时保留 LB 分项统计（`LimitBreak`）

## 关联模块
- core
- battle
- capture
- potency
- ui

## 历史记录
- [202601092006_dot_damage_accuracy](../../history/2026-01/202601092006_dot_damage_accuracy/) - DoT 统计口径修正
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 对齐 ACT：DPS/ENCDPS、LB、ActionEffect、多目标、DoT 归因
- [202601120627_act_damage_sync_v2](../../history/2026-01/202601120627_act_damage_sync_v2/) - 对齐 ACT：修复丢事件/PartyList 兜底/未知 DoT 漏算
- [202601121134_damage_parser_ref_deathbufftracker](../../history/2026-01/202601121134_damage_parser_ref_deathbufftracker/) - 参考 DeathBuffTracker：ActionEffectHandler 解析与签名
- [202601140004_dot_capture_disambiguate](../../history/2026-01/202601140004_dot_capture_disambiguate/) - DoT tick 按伤害匹配归因补强
- [202601141010_dot_sync_ff14mcp](../../history/2026-01/202601141010_dot_sync_ff14mcp/) - DotPot 改为支持从 ff14mcp 同步（补齐缺失 DoT 状态与威力）
