# Changelog

本文件记录 `helloagents/` 知识库与插件改动摘要，格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]
- 优化：窗口拖拽需按住 Alt，减少误触
- 优化：Summary 窗口显示与样式调整
- 新增：DPS/ENCDPS 口径切换（对齐 ACT：CombatantDuration vs EncounterDuration）
- 变更：默认 DPS 口径改为 ENCDPS（按战斗时长）
- 修复：Limit Break（LB）伤害归因与分项统计
- 修复：ActionEffect 多目标解析（按 Header.NumTargets/targetCount 解析）
- 修复：DoT 归因与未知来源 DoT 统计口径（含 sourceId 缓存/回退）
- 修复：DoT tick `buffId=0` 时的唯一匹配推断与技能明细展示（并提供 DoT 诊断开关与统计）
- 修复：DoT 威力表补齐 `statusId=1200`，避免高等级 DoT 分配漏算/误归因
- 修复：DoT 目标状态扫描改用 `ObjectTable.SearchByEntityId`，避免 `SearchById` 导致归因/推断失效
- 修复：DoT tick 过滤无效目标（`0xE0000000` 等）并加入同通道去重，避免 DoT 偏高与日志刷屏
- 新增：备选“ACTLike 归因”模式（DoT/召唤物）：OwnerCache 预热重试 + DoT 同 `statusId` 多来源按伤害匹配推断，减少复杂场景漏算/误归因
- 新增：PotencyUpdater 支持从 ff14mcp 同步 DotPot（`--ff14mcp-dots`）
- 更新：7.4 DoT 数据集（ff14-mcp）并同步 DotPot
- 优化：DoT tick `buffId=0` 且同源多 DoT 时按伤害匹配推断（`TryResolveDotBuffByDamage`）
- 修复：DoT tick 同时缺失 `sourceId`/`buffId` 时不再按伤害推断来源（移除 `TryResolveDotPairByDamage` 调用），改为先推断 `buffId`，再仅在状态表唯一时补齐来源
- 优化：`CalcDot` 在 DPP 不就绪时使用 fallback，避免 DoT 分配长时间不变动
- 修复：统计丢事件导致总伤害偏低（计入门槛放宽、PartyList 兜底建档、未知来源 DoT 无法扫描仍计入）
- 修复：参考 DeathBuffTracker，ActionEffect 改用 `ActionEffectHandler.Receive` 的签名与结构体解析，减少漏算与版本漂移
- 修复：ActionEffect 伤害值高位解码兼容（避免高伤害被截断为 16-bit）
- 更新：插件图标（统计图标重绘）
- 修复：同通道 DoT tick 可能出现 `buffId=0`/`buffId!=0` 双事件，去重避免 DoT 统计翻倍
- 修复：DoT 去重判定改用原始 `buffId`（推断补齐不影响判重），避免同 tick 双事件仍被重复计入
- 修复：跨通道 DoT 去重在 `buffId` 可确定且明确不一致时跳过去重，避免多 DoT 场景同伤害误删导致的 DoT 漏算
- 新增：`/act dotstats [log|file]` 输出 DoT 采集统计（Tick/模拟/合计，便于与 ACT 对照），可写入 `dalamud.log`/导出 `dot-debug.log`
- 新增：`/act dotdump [log|file] [all] [N]` 输出最近 DoT tick 事件（含去重/归因），可写入 `dalamud.log`/导出 `dot-debug.log`
- 新增：设置窗口 → DoT：一键写入日志/导出文件（不占用聊天栏）
- 优化：未知来源 DoT 仅推断 `buffId` 用于模拟分配，不再强行推断来源
- 优化：未开启“ACTLike 归因”时，DoT tick 来源推断默认优先扫描目标 `StatusList`，失败再回退缓存，减少缓存过期导致的错归因
- 新增：ACT.DieMoe MCP 通讯桥（ACT 插件 + MCP stdio server + Named Pipe IPC）
- 新增：ACT MCP `act_status` 附带当前遭遇快照（Top combatants：damage/encdps/dps），便于与 Dalamud 侧对齐
- 新增：ACT MCP `act_notify` 支持 `mcp:stats [top=N]` 触发一次性遭遇统计输出（配合 `act_log_tail` 拉取）
- 新增：`/act stats [log|file] [N]` 导出战斗统计快照（JSON），写入 `pluginConfigs/DalamudACT/battle-stats.jsonl` 便于 MCP/脚本对照 ACT
- 新增：`/act stats local|act|both ...` 支持强制本地/ACT/双快照导出（`both` 输出同一个 `pairId` 便于脚本对比）
- 变更：战斗结束自动导出固定为本地口径（`source=local`），保证无 ACT 环境可用
- 修复/对齐：补充 Network DoT tick 入口（ActionEffect `sourceId=0xE0000000`），与 Legacy 通道去重后归入 `DotEventCapture`，减少“不可见 DoT”导致的漏算
- 修复/对齐：遭遇开始/结束口径对齐 ACT `ActiveEncounter`（战斗事件驱动计时 + 5s 失活超时），不再依赖本地 `InCombat` 分段
- 修复/对齐：`ActionEffect` 检测到 damage-like effect 时也推进遭遇计时（仅更新 Start/Last/End，不写入玩家伤害），避免只剩敌方动作时过早分段
- 修复/对齐：`/act stats both file` 改为读取“战斗快照”避免并发不一致；当 ACT 快照不可用时 `file` 输出 JSON 诊断行，保证 `battle-stats.jsonl` 可持续解析
- 修复：`ResolveOwner` 优先对象表实时 owner（缺失回退缓存），降低 entityId 复用/缓存导致的召唤物错归因
- 新增：战斗结束自动导出 `battle-stats.jsonl`（`AutoExportBattleStatsOnEnd`）并提供设置窗口开关与“立即导出”按钮
- 新增：可选 ACT MCP 同步（`EnableActMcpSync` + `PreferActMcpTotals` + `ActMcpPipeName`），UI/导出可直接采用 ACT combatant 口径对齐总伤害/ENCDPS
- 修复：ACT MCP 返回中出现 `NaN/Infinity` 时的 JSON 解析失败（服务端读取侧清洗；插件侧也对 `EncDPS/DPS` 做有限值兜底）
