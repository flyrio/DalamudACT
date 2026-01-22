# 技术设计: 对齐 ACT 遭遇计时与 BattleStats 快照稳定化

## 技术方案
### 核心技术
- Dalamud Hook：`ActionEffectHandler.Receive`（ActionEffect 解析）
- 统计聚合：`ACTBattle`（Start/Last/End、伤害聚合）
- ACT 对照：Named Pipe JSON-RPC（`act/status`）

### 实现要点
1. **遭遇计时推进（ActionEffect damage-like）**
   - 在 `ACT2.Ability` 中，遍历 `TargetEffects[0..NumTargets)` 的 `Effects[0..8)`：
     - 若命中 `effect.Type is 3 or 5 or 6`（Damage/Blocked/Parried），则视为 damage-like effect
   - 命中后调用 `Battles[^1].MarkEncounterActivityOnly(eventTimeMs)`：
     - 仅更新 `StartTime/LastEventTime/EndTime`
     - 不新增/不修改任何玩家 `DataDic` 伤害数据
2. **BattleStats 快照化**
   - 在导出前于 `SyncRoot` 锁内构造 `BattleStatsSnapshot`：
     - 复制 `EffectiveStartTimeMs/EndTimeMs/DurationSeconds`
     - 复制每个 actor 的 `Name/JobId/BaseDamage/ActiveSeconds`
     - 复制 `DotDmgList/TotalDotDamage/TotalDotSim` 与 `LimitBreakDamage`
   - 导出时仅使用快照计算，避免并发写入导致枚举异常或局部不一致。
3. **ACT 对照“fresh”约束与 JSONL 诊断行**
   - `both` 模式：仅当本次即时拉取到 `latest`（且 combatants>0）时输出 `source=act_mcp`。
   - `file` 模式：缺少 ACT 快照时输出一条 JSON 诊断行（`error=no_fresh_encounter_snapshot`），避免写入非 JSON 文本破坏 JSONL。

## 安全与性能
- **安全:** 不新增外部网络访问；ACT 对照仍为可选 Named Pipe 读取
- **性能:** damage-like 扫描短路；BattleStats 快照仅在手动导出/自动导出时构造

## 测试与部署
- **构建:** `dotnet build DalamudACT.sln -c Release`
- **运行验证:**
  - 游戏内：`/act stats both file` 生成同 `pairId` 的 local/act 对照记录
  - 脱离 ACT：仅依赖本地统计仍可生成 `battle-stats.jsonl`
