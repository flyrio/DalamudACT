# 变更提案: DoT与召唤物伤害统计修复

## 需求背景
当前插件战斗统计普遍偏低，对照插件显示明显差距；怀疑 DoT tick 与召唤物伤害归因/统计丢失。

## 变更内容
1. 梳理 ActorControl/Ability 事件路径，修复 DoT/召唤物来源归属与去重。
2. 统一 DoT tick 计入总伤害与分项统计逻辑，避免漏计/重复计。
3. 补充 Buff->Action/Potency 映射并校验 DoT 模拟分配逻辑。
4. UI 端展示口径与统计口径一致化。

## 影响范围
- **模块:** core, battle, ui, potency
- **文件:** DalamudACT/ACT2.cs, DalamudACT/Struct/ACTBattle.cs, DalamudACT/PluginUI.cs, DalamudACT/Potency.cs
- **API:** 无
- **数据:** 无

## 核心场景

### 需求: 伤害统计准确性
**模块:** battle
修复 DoT 与召唤物统计偏低问题，使总伤害与对照插件接近。

#### 场景: DoT tick 归因
目标身上存在多个 DoT 状态时，tick 能准确归属到施放者。
- 预期结果: DoT tick 不漏计、不重复计
- 预期结果: UI 展示的 DoT 伤害与总伤害一致

#### 场景: 召唤物/宠物伤害归属
召唤物来源的伤害事件可归并到玩家。
- 预期结果: 召唤物伤害计入玩家总伤害
- 预期结果: 不因 owner 解析失败而丢失

## 风险评估
- **风险:** 归因修复可能导致重复统计或性能下降
- **缓解:** 增加去重/来源校验，限制状态扫描频率，保留回退路径
