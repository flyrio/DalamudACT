# battle

## 目的
聚合伤害事件并输出战斗统计口径（总伤害、DPS/ENCDPS、DoT 分配等）。

## 模块概述
- **职责**：维护 `ACTBattle`（遭遇）内的伤害/死亡/DoT 统计与持续时间口径
- **状态**：稳定
- **最后更新**：2026-01-29

## 关键设计

### DPS 口径（对齐 ACT）
- **ENCDPS**：`Damage / EncounterDuration`（战斗时长）
- **DPS**：`Damage / CombatantDuration`（个人活跃时长：该玩家首末次造成伤害的时间窗）
- **EncounterDuration**：对齐 ACT 的“结束延迟”表现：
  - 进行中遭遇：`EndTime = LastEventTime + Timeout`（预计结束时刻）
  - 同时使用 `EffectiveStartTime = StartTime + (EndTime - LastEventTime)` 做对齐，使得 `Duration = EndTime - EffectiveStartTime == LastEventTime - StartTime`，不包含延迟窗口
  - 遭遇结束后：`EndTime` 固化到 `LastEventTime`，`EffectiveStartTime` 回退为 `StartTime`

### 遭遇生命周期（对齐 ACT ActiveEncounter）
- 遭遇结束条件：`!InCombat && (now - LastEventTime > EncounterTimeoutMs)`（失活超时，可配置，默认 `30000ms`）
- 兜底：当 `InCombat=true` 但一段时间没有可计入的战斗事件（如 boss 转阶段上天/无敌），每隔约 1s 用 `InCombat` keepalive 推进 `LastEventTime`，避免误分段为两场战斗
- 空白遭遇丢弃：以 `HasMeaningfulData` 统一判定（玩家总伤害>0 / 有 LB / 或有 ACT 遭遇快照），避免“仅未知来源 DoT”或仅建档但 0 伤害的空白战斗挤占历史容量
- 计时推进由“战斗相关的行动/效果”驱动（对齐 ACT：避免转阶段/无敌期无伤害时过早分段）：
  - `ActionEffect` 中检测到 **damage-like effect**、或行动涉及 **BattleNpc（敌方/目标）**、或遭遇已开始且 `InCombat=true` 时，调用 `MarkEncounterActivityOnly` 推进 `StartTime/LastEventTime/EndTime`（即使来源不可归因/非玩家，也只推进计时，不写入任何玩家伤害）
  - `StartCast`：当来源为 BattleNpc（敌方）时也推进遭遇计时，用于覆盖“敌方动作但无伤害”的阶段
  - 真正计入统计的事件仍通过 `AddEvent` / DoT tick flush 写入伤害，并同步推进计时

### 可选：ACT MCP 同步（对齐总伤害/ENCDPS）
- **目的**：当本地统计在大规模战斗/特殊对象/召唤物归因等场景下与 ACT 存在偏差时，可直接以 ACT 口径作为权威来源，保证 UI/导出对齐
- **数据来源**：通过 `ActMcpPipeName`（默认 `act-diemoe-mcp`）调用 ACT MCP 的 `act/status`，并将遭遇快照写入 `ACTBattle.ActEncounter`
- **生效条件**：启用 `EnableActMcpSync` 且勾选 `PreferActMcpTotals`（配置 v21 起默认启用 `EnableActMcpSync=true`）
- **行为**：
  - UI：秒伤（ENCDPS/DPS）与总伤害优先使用 ACT 的 combatant 快照（与 `DpsTimeMode` 联动，避免“总伤害来自 ACT 但 DPS 仍按本地活跃时长计算”的口径偏差）
  - 遭遇计时：当 `PreferActMcpTotals=true` 且有 `ActEncounter` 快照时，同步 `StartTime/LastEventTime/EndTime` 到 ACT 口径（用于对齐遭遇起止与避免误分段）
  - 轮询策略：战斗中每秒拉取；非战斗状态降频（约 10s）以减少等待怪刷新时的无效开销；当当前战斗槽位尚未开始时会将快照写入上一场战斗，避免空白槽位被旧遭遇覆盖
  - 陈旧快照过滤：当 `now - ActEncounter.EndTimeMs > EncounterTimeoutMs + grace` 且 `InCombat=false` 时视为陈旧遭遇并清除；若仍处于战斗状态则保留快照，避免 Boss 转阶段/不可打导致 `endTime` 不更新而 UI 回退本地口径
  - 导出：
    - `/act stats local file`：强制导出本地口径（`source=local`）
    - `/act stats act file`：强制导出 ACT 口径（`source=act_mcp`，需快照可用）
    - `/act stats both file`：同时导出本地+ACT 两条 JSON（同一个 `pairId` 便于脚本对比）
    - 自动导出：当启用 `EnableActMcpSync` 时默认导出 `both`（`local` + `act_mcp`，同一个 `pairId`）；未启用时仍为 `local`
    - `act_mcp` 行附带字段 `actSnapshotOnDemand`：`true` 表示本次导出成功 on-demand 拉取，`false` 表示回退使用缓存快照
    - `act_mcp` 行附带字段 `actTopCombatants`：ACT 侧 Top30 combatants 摘要（包含非玩家/宠物），用于排查“宠物/地面 DoT 是否独立计入”等对齐差异
    - `act_mcp` 行的 per-actor 字段 `actDotDamage`：ACT 侧 `dotDamage`（用于核对 DOT 占比）
    - `act_mcp` 行字段 `totalDotDamage`：对当前导出 `actors` 集合的 `actDotDamage` 求和（actors 基于 ACT combatants 生成，便于直接核对 DOT 占比）
    - 为避免两套口径混用：`act_mcp` 行会将本地侧的 `dotTickDamage/dotTickCount/dotSkillDamage/dotTotalOnly*` 置为 0，仅保留 `actDotDamage` 作为权威 DoT 数据源
    - `local` 行字段 `totalDotDamage`：包含 **已归因 DoT tick 总量**（`sum(dotTickDamage)`）+ **未知来源 DoT 总量**（`TotalDotDamage`），用于与 ACT 的 `dotDamage` 总量对齐
    - `local` 行字段 `totalDamageAll`：当启用 DoT 模拟分配时，除 `dotSimDamage` 已分配部分外，还会补入“未分配的未知来源 DoT”（避免总伤害因分配覆盖率不足而偏低）

### 事件计入门槛（避免丢事件）
- 不再依赖 `LocalPlayer.StatusFlags.InCombat` 作为硬门槛
- 计入条件（遭遇未开始时）：允许由**任意玩家**（`sourceId<=0x40000000`）或 **未知来源 DoT tick**（`0xE0000000`）启动；本地 `ConditionFlag.InCombat` 作为兜底
- 目的：对齐 ACT 的 ActiveEncounter（中途加入/脱战波动/怪物先手等场景下计时更一致）

### DoT 处理（tick 采集与归因）
- **入口**：`ACT2.ReceiveActorControlSelf` 捕获 DoT tick → `DotEventCapture` 统一排队与去重 → `ACTBattle` 写入统计
- **补充入口（Network）**：基于 `ActionEffectHandler.Receive` 的 ActionEffect 事件补齐 DoT tick：
  - `sourceId==0xE0000000` 且 `actionId` 命中 DoT 状态表时（或启用增强归因后，目标状态表存在该 `statusId`），视为“未知来源 DoT tick”入队（`DotTickChannel.NetworkActorControlSelf`），用于覆盖 ActorControlSelf 不完整/不可见的场景，并依赖 `DotEventCapture` 的跨通道去重避免翻倍
  - `sourceId!=0xE0000000` 且 `actionId` 命中 DoT 状态表时，视为“来源明确的 DoT tick”入队（`DotTickChannel.NetworkActorControl`），避免被当作普通技能伤害计入 `baseDamage` 而导致 DoT 统计偏低
- **已知来源 DoT**：
  - 总伤害：计入来源玩家总伤害（`AddEvent`/`AddDotDamage`），并刷新个人活跃时长
  - Tick 统计：累计到 `DotDamageByActor` / `DotTickCountByActor`；其中 `TotalOnly`（未识别技能/状态）另计入 `DotTotalOnlyDamageByActor` / `DotTotalOnlyTickCountByActor`（Tooltip 会提示“未识别DOT tick”）
  - 技能明细（可选）：当启用 `EnableEnhancedDotCapture` 且可确定 `buffId` 时，按 `statusId` 计入技能明细，便于查看 DoT 每 tick 变化
- **buffId=0 修复（唯一匹配）**：当 tick 报文缺失 `buffId` 且来源已知时，尝试在目标 `StatusList` 中按 `(targetId, sourceId)` 唯一匹配一个 DoT 状态（`TryResolveDotBuff`）；多 DoT 同源则不推断，避免误归因
- **buffId=0 修复（按伤害匹配）**：当来源已知但同源存在多个 DoT（如诗人双 DoT）导致唯一匹配失败时，使用 tick 实际伤害 + 该玩家 DPP + DoT 威力做保守选择（`TryResolveDotBuffByDamage`），仅在差异足够大时才归因
- **buffId=0 防御（避免错归因）**：当 tick 最终仍为 `buffId=0` 且 `sourceId` 已知时，只有当目标身上存在该来源的任意 DoT 状态（`HasAnyDotFromSource`）才允许将该 tick 计入该来源；否则回退为“未知来源 DoT”，避免把机制 tick/缺字段 tick 错计到不相关职业名下
- **DoT 威力来源**：DoT 威力表（`Potency.DotPot`）可通过 PotencyUpdater 的 `--ff14mcp-dots` 从 ff14mcp 的 `dots_by_job.json` 同步，减少版本漂移与手工维护
- **未知来源 DoT**：
  - 始终计入 `TotalDotDamage`（即使无法读取目标状态列表）
  - 若可读取目标状态：扫描目标 `StatusList` 收集可分配 DoT 状态（含来源/威力），并通过 `CalcDot` 更新 `DotDmgList` 做模拟分配
  - 若不可读取目标状态：仍触发 `CalcDot` 以保持 `TotalDotDamage` 与分配结果一致，避免总伤害偏低
- **未知来源 DoT（按伤害匹配）**：当 tick 同时缺失 `sourceId` 与 `buffId` 时，启用增强归因后可尝试用目标状态表候选 + tick 伤害做一次 `(sourceId,buffId)` 配对消歧；若仍无法唯一归因，则回退到“推断 buffId/或模拟分配”的兜底路径，避免误归因扩大
- **候选范围（对齐 ACT）**：目标 `StatusList` 的候选扫描不再限制“仅本地/队伍”，允许对大型战斗中的其他玩家 DoT 进行推断归因（以减少未知来源 DoT 的模拟分配占比）
- **来源推断（避免错归因扩大）**：当 `buffId!=0` 但 `sourceId` 缺失时，仅在目标状态表唯一匹配或“按伤害匹配”满足阈值时才归因；不再回退到 `(targetId,buffId)->sourceId` 缓存来源，避免同 `statusId` 多来源并存时把 tick 错算到某个玩家
- **去重口径**：
  - 同通道完全重复：过滤重复回调/重复包（窗口 200ms）
  - 跨通道重复：Legacy/Network 双通道重复时丢弃（窗口 800ms）；当两侧 `buffId`（原始或推断）均可确定且明确不一致时跳过去重，避免不同 DoT 同伤害误删
  - 同通道双事件：当同一 tick 同时出现 `buffId=0` 与 `buffId!=0` 两条事件时视为重复（窗口 800ms，判重使用原始 `buffId`，不受推断补齐影响），避免 DoT 翻倍；同毫秒内优先保留带 `buffId` 的事件
- **模拟分配兜底（DPP 不就绪）**：`CalcDot` 会用已知玩家 DPP 的平均值作为 fallback，让分配更连续，避免未记录基准技能导致 DoT 长时间不变动
- **不可分配 DoT（目标无状态/缺表）**：当未知来源 DoT tick 的目标无法扫描到可分配的 DoT（`ActiveDots=0` 或无法读取目标状态）时，该 tick 计入 `UndistributableDotDamage`，仅表现为 `UnassignedDotDamage`，避免把地面 DoT/缺表 DoT 错分配到其他玩家
- **诊断口径**：
  - `EnableDotDiagnostics` 开启后在设置窗口展示入队/处理/去重/未知来源/推断次数，并输出 Verbose 日志
  - 命令 `/act dotstats [log|file]`：输出采集统计 + 自己 DoT 的 Tick/模拟/合计（便于与 ACT 的 Simulated DoTs 对照）
    - 默认输出到聊天栏；`log` 写入 `dalamud.log`（Info 级）；`file` 导出到 `pluginConfigs/DalamudACT/dot-debug.log`
    - 统计项包含 `拒绝src(目标无DoT)`：表示“buffId=0 且目标身上不存在该来源 DoT”而被回退为未知来源的 tick 次数
  - 命令 `/act dotdump [log|file] [all] [N]`：输出最近 DoT tick 事件（含去重/归因），用于定位错归因/重复统计
    - 默认输出到聊天栏；`log` 写入 `dalamud.log`；`file` 导出到 `pluginConfigs/DalamudACT/dot-debug.log`
    - 当触发“目标无该来源 DoT → 回退未知来源”时会输出 `DROP=RejectedSourceNotOnTarget`，便于在日志中直接定位
  - 设置窗口 → DoT：提供按钮一键写入日志/导出文件（不占用聊天栏）
  - 命令 `/act stats [local|act|both] [log|file|chat] [N]`：导出当前战斗统计快照（JSON），用于与 ACT 对照
    - `file`：写入 `pluginConfigs/DalamudACT/battle-stats.jsonl`（推荐，便于 MCP/脚本读取）
    - `log`：写入 `dalamud.log`（单行 JSON）
    - `both`：当 ACT 快照不可用时，`file` 模式会写入一条 `source=act_mcp` 的错误 JSON（`error=no_encounter_snapshot`），保证 JSONL 可持续解析
    - 导出字段（per-actor）包含 `dotTickDamage/dotTickCount`、`dotSkillDamage`、`dotTotalOnlyTickDamage/dotTotalOnlyTickCount`，便于定位“tick 捕获 vs 技能汇总”差异
  - 配置 `AutoExportBattleStatsOnEnd`：战斗结束自动导出 `battle-stats.jsonl`（降低对齐调试成本）
  - 配置 `AutoExportDotDumpOnEnd`：战斗结束自动导出 `DoTStats` + `DotDump(all)` 到 `pluginConfigs/DalamudACT/dot-debug.log`（用于离线核对 DoT 归因差异）
    - `AutoExportDotDumpMax`：DotDump 最大输出条数（默认 200；范围 20~2000）

### 备选模式：ACTLike 归因（DoT/召唤物）
- **开关**：配置 `EnableActLikeAttribution`（设置窗口 → DoT）
- **召唤物/Owner 合并**：
  - 默认：仅将 `BattleNpcSubKind.Pet` 合并到玩家名下（更保守，避免误归因）
  - ACTLike：只要对象 `OwnerId` 是有效玩家（`<=0x40000000`），就归并到玩家名下（覆盖部分地面/机制对象导致的漏算）
  - 缓存带 TTL（默认 2.5s），降低 entityId 复用导致的错归因
  - `ResolveOwner` 优先对象表实时 owner，缺失时回退“短期缓存”；当解析失败会预热重试，减少漏算与错归因
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
- [202601220445_act_encounter_sync](../../history/2026-01/202601220445_act_encounter_sync/) - 对齐 ACT ActiveEncounter：遭遇计时与生命周期
- [202601221613_act_encounter_activity_snapshot](../../history/2026-01/202601221613_act_encounter_activity_snapshot/) - 对齐 ACT：遭遇计时推进 + BattleStats 快照稳定化
- [202601290237_dot_stats_skill_based](../../history/2026-01/202601290237_dot_stats_skill_based/) - DOT 统计口径改为技能明细汇总（未识别 tick 追踪/导出）
- [202601290301_dot_tick_source_guard](../../history/2026-01/202601290301_dot_tick_source_guard/) - DoT tick 防御：目标无该来源 DoT 时回退未知来源（避免错归因）
- [202601290336_dot_history_act_stale_fix](../../history/2026-01/202601290336_dot_history_act_stale_fix/) - 历史战斗空白覆盖修复 + DoT 归因更保守 + ACT dotDamage 探测
- [202601291120_dot_align_act_mcp_fix](../../history/2026-01/202601291120_dot_align_act_mcp_fix/) - DoT 对齐 ACT：dotDamage 参照修复 + 捕获归因补强 + 历史稳定
- [202601291140_act_dotdamage_align_v2](../../history/2026-01/202601291140_act_dotdamage_align_v2/) - ACT dotDamage 提取与 DOT 占比对齐补强
- [202601291554_dot_align_act_v3](../../history/2026-01/202601291554_dot_align_act_v3/) - DoT 对齐 ACT：不覆盖可信来源 + 候选扩展 + UI DOT 总量显示
