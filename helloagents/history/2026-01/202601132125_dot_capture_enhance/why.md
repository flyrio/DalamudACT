# 变更提案: DoT tick 归因与明细增强

## 需求背景
当前插件的 DoT 伤害统计存在明显异常：
- DoT 伤害数值更新不随 tick（约每 3 秒）实时增长，表现为“长时间不变/偶尔才跳一下”。
- 日志中频繁出现 `Dot:0`（`buffId` 缺失）与 `source=E0000000`（来源未知）等形态，导致 DoT 只能进入“模拟补正”或仅计入总伤害，用户侧观感为偏低/不连续。

现有实现依赖 `ActorControlSelf` 路径解析 DoT tick（`ACT2.ReceiveActorControlSelf`），再由 `ACTBattle` 聚合统计。DoT tick 报文在部分情况下会缺失 `buffId`（常见为 0），导致无法稳定输出“哪个 DoT 技能在跳伤害”的明细。

## 变更内容
1. **统一 DoT tick 事件模型**：抽象 `DotTickEvent`（sourceId/targetId/buffId/damage/time）并集中处理，避免多处重复逻辑。
2. **增强归因（buffId=0 唯一匹配推断）**：当来源已知但 `buffId=0` 时，扫描目标 `StatusList`，按 `(targetId, sourceId)` 做 DoT 状态的唯一匹配推断（多 DoT 同源则不推断，避免误归因）。
3. **技能明细输出（可选）**：启用 `EnableEnhancedDotCapture` 后，将可确定 `buffId` 的 DoT tick 按 `statusId` 计入技能明细，使学者/白魔等 DoT 能随 tick 在技能列表中连续增长。
4. **去重与一致性**：引入小窗口去重缓存，避免未来多入口并存时重复计入；并保持 `ACTBattle` 战斗时长/个人活跃时长口径一致。
5. **可观测性增强**：增加 DoT 诊断统计与可控 Verbose 日志开关，便于定位版本漂移与归因失败原因。

> 备注：原方案中计划引入 `IGameNetwork`/网络层采集入口作为“增强采集”，但 SDK 已将 `IGameNetwork` 标记为 `Obsolete(error)`；本次实现改为在 legacy 路径上做增强归因与明细输出（更稳妥、可编译）。

## 影响范围
- **模块**：core、battle、ui
- **文件**：
  - `DalamudACT/DotEventCapture.cs`
  - `DalamudACT/ACT2.cs`
  - `DalamudACT/Struct/ACTBattle.cs`
  - `DalamudACT/Struct/ActorControl.cs`
  - `DalamudACT/Configuration.cs`
  - `DalamudACT/PluginUI.cs`
  - `helloagents/wiki/modules/battle.md`
  - `helloagents/CHANGELOG.md`

## 核心场景
- 本地学者/队友白魔 DoT：DoT tick 随服务器 tick（约 3 秒）持续增长，且在“技能明细”中可观察到对应 DoT 状态的伤害累计。
- `buffId=0`：来源已知时通过唯一匹配补齐，来源未知时不阻断统计（仍计入总量并可诊断）。
