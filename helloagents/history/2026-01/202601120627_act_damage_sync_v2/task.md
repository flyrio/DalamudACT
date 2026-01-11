# 轻量迭代任务：act_damage_sync_v2

时间：202601120627

目标：继续对齐 ACT 口径，修复“总伤害偏低/丢事件”导致的 DPS 与 ACT 不一致。

## 任务清单

- [√] 修复事件计入门槛：不再依赖 LocalPlayer.StatusFlags.InCombat，避免死亡/脱战边界丢事件
- [√] 修复玩家建档失败丢伤害：支持 PartyList 兜底 + 占位建档，保证伤害不因“找不到对象表”而丢弃
- [√] 修复未知来源 DoT：即使无法扫描目标状态也要计入 TotalDotDamage，避免漏算
- [√] 编译验证：`dotnet build DalamudACT.sln -c Release`
