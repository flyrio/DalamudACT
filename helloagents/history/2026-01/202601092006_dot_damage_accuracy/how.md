# 技术设计: DoT与召唤物伤害统计修复

## 技术方案
### 核心技术
- C# / Dalamud Hook / ActorControlSelf 与 ReceiveAbility
- 状态列表解析 (IBattleNpc.StatusList)
- Potency 与 Buff->Action 映射

### 实现要点
- 在 ActorControlSelf DoT 事件中统一解析 arg0/arg1/arg2/targetId。
- 当来源为 0 或不可用时，通过目标状态列表匹配 BuffId -> SourceId。
- 对召唤物/宠物来源先做 OwnerId 映射，必要时降级为玩家。
- 在 ACTBattle 中统一 DoT tick 计入 Total 与 DotDamageByActor 的口径，避免重复统计。
- Dot 模拟仅作为补正显示，确保与总伤害计算一致。
- UI 端对 DoT 显示与合成逻辑做一致性调整。

## 安全与性能
- **安全:** 不涉及外部服务与敏感数据。
- **性能:** 状态扫描仅在 DoT tick 触发且来源缺失时执行，缓存最近一次匹配结果。

## 测试与部署
- **测试:** 对照同一场战斗的总伤害/DPS；优先选择 DoT/召唤物占比高的职业。
- **部署:** 本地构建或 Dalamud 热载验证。
