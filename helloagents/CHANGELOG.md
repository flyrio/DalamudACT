# Changelog

本文件记录 `helloagents/` 知识库与插件改动摘要，格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]
- 优化：窗口拖拽需按住 Alt，减少误触
- 优化：Summary 窗口显示与样式调整
- 新增：DPS/ENCDPS 口径切换（对齐 ACT：CombatantDuration vs EncounterDuration）
- 修复：Limit Break（LB）伤害归因与分项统计
- 修复：ActionEffect 多目标解析（按 Header.NumTargets/targetCount 解析）
- 修复：DoT 归因与未知来源 DoT 统计口径（含 sourceId 缓存/回退）
- 修复：DoT tick `buffId=0` 时的唯一匹配推断与技能明细展示（并提供 DoT 诊断开关与统计）
- 修复：DoT 威力表补齐 `statusId=1200`，避免高等级 DoT 分配漏算/误归因
- 新增：PotencyUpdater 支持从 ff14mcp 同步 DotPot（`--ff14mcp-dots`）
- 更新：7.4 DoT 数据集（ff14-mcp）并同步 DotPot
- 优化：DoT tick `buffId=0` 且同源多 DoT 时按伤害匹配推断（`TryResolveDotBuffByDamage`）
- 优化：DoT tick 同时缺失 `sourceId`/`buffId` 时按伤害匹配推断（`TryResolveDotPairByDamage`），失败回退到模拟分配
- 优化：`CalcDot` 在 DPP 不就绪时使用 fallback，避免 DoT 分配长时间不变动
- 修复：统计丢事件导致总伤害偏低（计入门槛放宽、PartyList 兜底建档、未知来源 DoT 无法扫描仍计入）
- 修复：参考 DeathBuffTracker，ActionEffect 改用 `ActionEffectHandler.Receive` 的签名与结构体解析，减少漏算与版本漂移
- 更新：插件图标（统计图标重绘）
- 修复：DoT 去重仅跨通道生效，避免未知来源/同通道误删导致总伤害偏低
- 优化：未知来源 DoT 仅推断 `buffId` 用于模拟分配，不再强行推断来源
