# 技术设计: ACT 对齐的备选 DoT/召唤物归因

## 1. 现状概述（Ground Truth）
- DoT tick 入口：`ActorControlCategory.DoT (0x605)` → `DotEventCapture` → `ACTBattle`
- 召唤物伤害：`Ability(ActionEffect)` 中对 `sourceId>0x40000000` 做 `ResolveOwner` 合并到主人；若 Owner 解析失败会直接丢弃该事件

## 2. 问题假设（与现象对应）
### 2.1 召唤物漏算
在对象表缺失/时序竞争等情况下，`ResolveOwner(petId)` 可能返回 `0xE0000000`，导致对应伤害事件被丢弃。

### 2.2 DoT 误归因/明细错误
复杂场景下 DoT tick 报文可能出现：
- `sourceId` 缺失/不可用（如 `0`、`0xE0000000`、或无法可靠映射到玩家）
- `buffId` 缺失（`0`）或同目标存在多个同 `statusId` 的来源（多人同职业）

## 3. 方案概要（备选模式：ACTLike）
新增配置开关 `ACTLike 归因（备选）`，启用后采用更“保守但更贴近 ACT” 的归因策略：

### 3.1 召唤物伤害合并（Owner 解析增强）
1. 引入“OwnerCache 预热”：
   - 周期性扫描 `ObjectTable`，缓存 `entityId(>0x40000000) -> ownerId(<=0x40000000)` 映射
2. 在伤害事件解析时：
   - 若 `ResolveOwner` 失败，则触发一次预热并重试解析，尽量避免事件被丢弃

### 3.2 DoT tick 归因（来源优先级 + 伤害匹配）
对每个 DoT tick 事件按以下优先级处理：
1. **报文来源可用**：`sourceId` 合法 → 直接计入来源（并合并召唤物）
2. **状态唯一匹配**：当 `buffId!=0` 且 `StatusList` 中该 `statusId` 仅存在唯一来源 → 归因该来源
3. **同 statusId 多来源时的伤害匹配（关键新增）**：
   - 候选来源：目标 `StatusList` 中 `statusId=buffId` 的所有来源（合并召唤物后）
   - 以各来源 DPP 与 DoT 威力表预测 tick 伤害区间（包含 crit/dh 倍率容错）
   - 仅在误差足够小且与第二名拉开差距时才归因（否则视为未知来源，避免误归因）
4. **兜底**：无法唯一归因时，沿用现有“未知来源 DoT”口径：计入 `TotalDotDamage` 并按目标状态模拟分配

### 3.3 buffId/sourceId 同时缺失（可选增强）
在备选模式下允许尝试用“目标状态 + 伤害匹配”同时推断 `(sourceId,buffId)`：
- 仅在满足严格阈值与可分离条件时才归因
- 失败则回退到模拟分配

## 4. 配置与可观测性
- UI 增加开关：`启用 ACTLike 归因（备选：DoT/召唤物）`
- 复用现有 DoT 诊断计数，必要时补充“按伤害匹配归因次数”等计数

## 5. 风险与规避
- 风险：伤害匹配在 DPP 不稳定或多来源伤害非常接近时可能误归因
  - 规避：严格阈值 + 分离度要求；不满足则不归因，回退模拟分配
- 风险：对象表预热扫描增加开销
  - 规避：节流（间隔触发）+ 仅在需要时/战斗中触发

## 6. ADR
### ADR-20260121-actlike-attribution
- 决策：以“可切换的备选归因模式”提供 ACTLike 行为，默认保持现状
- 理由：复杂场景存在不确定性，提供 A/B 与回滚通道最稳妥

