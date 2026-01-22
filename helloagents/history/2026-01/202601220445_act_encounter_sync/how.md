# 技术设计: 对齐 ACT 遭遇计时与生命周期（ActiveEncounter）

## 技术方案

### 核心技术
- .NET / Dalamud Hook（ActionEffectHandler、ActorControlSelf）
- 基于事件时间戳的遭遇生命周期（失活超时）

### 实现要点
1. **遭遇计时统一入口**
   - 在 ActionEffect 解析到“伤害类 effect（Damage/Blocked/Parried）”时，调用 `ACTBattle.MarkEncounterActivityOnly(timeMs)` 标记遭遇活动。
   - 该标记不修改任何玩家伤害数据，仅同步 `StartTime/EndTime/LastEventTime`。

2. **遭遇结束策略（对齐 ACT ActiveEncounter）**
   - 不再依赖本地 `ConditionFlag.InCombat` 作为结束条件。
   - 若 `now - battle.LastEventTime > EncounterTimeoutMs(6000ms)`，则认为遭遇结束：
     - 固化 `EndTime = LastEventTime`
     - 清理 `ActiveDots`、`OwnerCache`
     - 轮转战斗列表并重置 DoT transient caches

3. **统计进行中遭遇的 EndTime 推进**
   - 遭遇未结束且已开始时，将 `EndTime` 持续推进到 `now`，使进行中 ENCDPS 与 ACT 的“进行中遭遇”一致。

4. **Owner 解析一致性**
   - `ResolveOwner` 优先读取对象表的实时 OwnerId，避免缓存优先导致的错归因（entityId 复用/缓存未刷新）。
   - 对象表缺失时回退缓存，用于处理对象表时序竞争。

5. **自动导出（对齐调试）**
   - 新增配置 `AutoExportBattleStatsOnEnd`。
   - 当遭遇结束时自动调用 `PrintBattleStats(File)`，向 `pluginConfigs/DalamudACT/battle-stats.jsonl` 追加一条 JSONL 记录。

## 安全与性能
- **安全:** 不进行任何网络注入/敏感操作；仅在插件侧调整统计口径与导出行为。
- **性能:**
  - OwnerCache 预热有 500ms 节流；
  - 遭遇结束导出为“每场一次”，避免高频 IO。

## 测试与部署
- **测试:**
  - `dotnet build DalamudACT.sln -c Release`
  - 真实战斗中对比 ACT `act_status` 与 `battle-stats.jsonl` 的 duration、damage、encdps。
- **部署:** 重新加载 Dalamud 插件（或重启游戏/重载插件管理器）。

