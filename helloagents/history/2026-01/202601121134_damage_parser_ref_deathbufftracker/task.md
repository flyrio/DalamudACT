# 轻量迭代任务：damage_parser_ref_deathbufftracker

时间：202601121134

目标：参考 `D:\DeathBuffTracker` 的 ActionEffect 钩子/结构体解析方式，提升本插件伤害统计捕获的稳定性与精确度。

## 任务清单

- [√] 将 ActionEffect 解析切换为 `FFXIVClientStructs.FFXIV.Client.Game.Character.ActionEffectHandler`（Header/TargetEffects/Effect）
- [√] 将 ActionEffect Hook 切换为 `ActionEffectHandler.Addresses.Receive.String`（减少签名漂移）
- [√] 修正/简化目标遍历：不因 targetId==0 提前 break，避免漏算
- [√] 编译验证：`dotnet build DalamudACT.sln -c Release`
