# task

- [√] 修正 Tooltip 的 DOT 伤害统计口径（技能明细汇总 + 模拟补正，Tick 仅诊断）
- [√] 记录并展示/导出 TotalOnly（未识别技能/状态）的 DoT tick
- [√] 调整 DotEventCapture Flush 排序（优先处理来源明确事件）以提升跨通道去重稳定性
- [√] BattleStats JSONL 导出追加 per-actor dotSkillDamage/dotTotalOnlyTickDamage 等字段
- [√] Release 构建验证
