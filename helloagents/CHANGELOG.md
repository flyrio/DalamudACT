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
- 优化：DoT tick 同时缺失 `sourceId`/`buffId` 时按伤害匹配推断（`TryResolveDotPairByDamage`），失败回退到模拟分配
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
