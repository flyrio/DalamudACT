# 轻量迭代任务清单：act_damage_sync

时间戳: 202601120554

目标: 同步伤害统计口径，使插件伤害与 ACT 更一致（重点：LB、ActionEffect 目标解析、DoT 来源回退）。

## 任务

- [√] 复盘差异点：确认 LB 未计入玩家总伤害、ActionEffect 多目标解析存在潜在尾部脏数据风险
- [√] 调研参考：OverlayPlugin 导出的 Combatant/Encounter 字段含义（DPS vs ENCDPS；damage 口径）
- [√] 修复：LB 伤害计入施放者总伤害，同时保留 LB 分项用于展示
- [√] 修复：ActionEffect 解析按 Header.NumTargets 精确处理
- [√] 优化：DoT 来源缓存（targetId+buffId → sourceId），降低未知来源 DoT 的误分配
- [√] 验证：`dotnet build DalamudACT.sln -c Release`

