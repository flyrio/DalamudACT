# 变更提案: DoT 归因偏差修复

## 需求背景
近期在「伤害统计（DalamudACT）」中观察到战斗统计与 ACT/FFLogs 口径不一致，尤其是 DoT 相关伤害的归属出现明显偏差：

- 同一场遭遇中，插件显示的个人总伤害显著低于 ACT 的 Outgoing Damage。
- 部分情况下他人（同队或同场景玩家）伤害异常偏高/偏低，怀疑 DoT tick 的归属被错误分配。

基于当前运行时数据（`/act dotdump`）可确认：大量 DoT tick 报文会缺失 `buffId`（显示为 `buff=0->xxx`，依赖推断补齐），在少数情况下 `sourceId`/`buffId` 可能同时缺失。当前实现存在“按伤害推断来源”的逻辑，可能在复杂场景下造成严重错归因。

## 变更内容
1. 调整 DoT tick 归因的推断顺序与策略：优先补齐 `buffId`，再基于目标状态表做**唯一来源**解析；避免在信息不足时“强行按伤害推断来源”。
2. 修复 `buffId` 被补齐后未再次尝试解析 `sourceId` 的缺口，减少落入“未知来源 DoT → 模拟分配”的比例。
3. 保持现有去重与诊断命令不变，确保可用 `/act dotstats`、`/act dotdump` 验证效果。

## 影响范围
- **模块:**
  - DoT 采集与归因（`DotEventCapture`）
  - 战斗聚合与 DoT 分配口径（`ACTBattle` 的相关接口不变，行为更一致）
- **文件:**
  - `DalamudACT/DotEventCapture.cs`
  - `helloagents/wiki/modules/battle.md`
  - `helloagents/CHANGELOG.md`

## 核心场景

### 需求: DoT tick 归属准确
**模块:** battle

#### 场景: tick 缺失 `buffId`
当 DoT tick 报文缺失 `buffId`（`buffId=0`）但 `sourceId` 存在时，应尽可能通过目标 `StatusList` 推断 `buffId`，并将 tick 计入来源玩家总伤害（至少 TotalOnly）。

#### 场景: tick 同时缺失 `sourceId`/`buffId`
当 tick 同时缺失 `sourceId`/`buffId` 时，应仅推断 `buffId`（用于模拟分配），并在推断出 `buffId` 后尝试通过目标 `StatusList` 唯一匹配来源；不得使用“按伤害推断来源”作为默认路径，避免错归因扩大。

#### 场景: 多 DoT 同目标并存
当同一目标上存在多 DoT（多来源或同源多 DoT）并存时，归因策略应尽量保守：能唯一确定则归因，无法唯一确定则回退到未知来源统计与模拟分配，避免把 tick 错记到错误玩家。

## 风险评估
- **风险:** 调整归因策略可能改变部分极端场景下的分配结果（从“强行归因”变为“保守回退”），表现为部分玩家 DoT 伤害短期下降但总体更可信。
- **缓解:** 保留诊断命令与计数，提供可回滚的逻辑边界；以 ACT 对照验证“错归因减少/漏算减少”为验收标准。

