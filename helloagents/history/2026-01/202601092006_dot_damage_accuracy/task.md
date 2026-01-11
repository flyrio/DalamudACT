# 任务清单: DoT与召唤物伤害统计修复

目录: `helloagents/plan/202601092006_dot_damage_accuracy/`

---

## 1. core 事件归因
- [√] 1.1 在 `DalamudACT/ACT2.cs` 中完善 ActorControlSelf DoT 路径的来源解析与回退归属，验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因
- [√] 1.2 在 `DalamudACT/ACT2.cs` 中补充召唤物/宠物 OwnerId 解析与异常来源处理，验证 why.md#需求-伤害统计准确性-场景-召唤物-宠物伤害归属，依赖任务1.1

## 2. battle 统计一致性
- [√] 2.1 在 `DalamudACT/Struct/ACTBattle.cs` 中统一 DoT 计入 Total/DotDamageByActor 的口径并避免重复，验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因
- [√] 2.2 在 `DalamudACT/Struct/ACTBattle.cs` 中校正 DotSim 与总伤害的关系，确保 UI 只做补正显示，验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因，依赖任务2.1

## 3. ui 展示口径
- [√] 3.1 在 `DalamudACT/PluginUI.cs` 中调整 DoT 显示与总伤害合成逻辑，避免重复叠加，验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因
- [√] 3.2 在 `DalamudACT/PluginUI.cs` 中完善 tooltip 的 DoT 分项来源说明，验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因

## 4. potency 数据校验
- [-] 4.1 在 `DalamudACT/Potency.cs` 中核对 BuffToAction/DotPot 覆盖范围，补齐缺失条目（如有），验证 why.md#需求-伤害统计准确性-场景-dot-tick-归因
> 备注: 未提供可用的 Potency 数据源，暂未更新。

## 5. 安全检查
- [√] 5.1 执行安全检查（输入校验、来源归属、避免重复统计、EHRB规避）

## 6. 文档更新
- [√] 6.1 更新 `helloagents/wiki/modules/core.md`
- [√] 6.2 更新 `helloagents/wiki/modules/battle.md`
- [√] 6.3 更新 `helloagents/wiki/modules/ui.md`
- [√] 6.4 更新 `helloagents/wiki/modules/potency.md`

## 7. 测试
- [-] 7.1 在游戏内选取 DoT/召唤物占比高的职业进行对照测试，记录总伤害/DPS差距
> 备注: 当前环境无法进行游戏内对照测试。
