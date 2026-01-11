# Changelog

本文件记录 `helloagents/` 知识库与插件改动摘要，格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]
- 优化：窗口拖拽需按住 Alt，减少误触
- 优化：Summary 窗口显示与样式调整
- 新增：DPS/ENCDPS 口径切换（对齐 ACT：CombatantDuration vs EncounterDuration）
- 修复：Limit Break（LB）伤害归因与分项统计
- 修复：ActionEffect 多目标解析（按 Header.NumTargets/targetCount 解析）
- 修复：DoT 归因与未知来源 DoT 统计口径（含 sourceId 缓存/回退）
- 修复：统计丢事件导致总伤害偏低（计入门槛放宽、PartyList 兜底建档、未知来源 DoT 无法扫描仍计入）

