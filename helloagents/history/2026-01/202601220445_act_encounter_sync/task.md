# 任务清单: 对齐 ACT 遭遇计时与生命周期（ActiveEncounter）

目录: `helloagents/plan/202601220445_act_encounter_sync/`

---

## 1. 遭遇计时与生命周期
- [√] 1.1 在 `DalamudACT/ACT2.cs` 中改造遭遇结束策略为“最后事件 + 6000ms 超时”，并在进行中推进 `EndTime`
- [√] 1.2 在 `DalamudACT/ACT2.cs` 的 ActionEffect 入口增加“伤害类 effect”触发的 `MarkEncounterActivityOnly`（仅计时）

## 2. 归因与缓存一致性
- [√] 2.1 在 `DalamudACT/Struct/ACTBattle.cs` 新增 `MarkEncounterActivityOnly`（对外仅计时）
- [√] 2.2 在 `DalamudACT/Struct/ACTBattle.cs` 调整 `ResolveOwner`：优先对象表实时 owner，缺失时回退缓存

## 3. 导出与对齐调试
- [√] 3.1 在 `DalamudACT/Configuration.cs` 增加 `AutoExportBattleStatsOnEnd` 并迁移版本
- [√] 3.2 在 `DalamudACT/PluginUI.cs` 增加导出开关与“立即导出”按钮
- [√] 3.3 在 `DalamudACT/ACT2.cs` 中在遭遇结束时自动导出 `battle-stats.jsonl`

## 4. 安全检查
- [√] 4.1 确认未引入危险网络注入、未写入敏感信息、未新增高风险命令调用

## 5. 构建与验证
- [√] 5.1 `dotnet build DalamudACT.sln -c Release` 编译通过
- [?] 5.2 真实战斗结束后：对比 ACT MCP `act_status` 与 `battle-stats.jsonl`（duration/dmg/encdps）
  > 备注: 需在游戏内加载新版插件后进行；如无法通过 MCP 发送插件命令，请手动重载插件或使用设置窗口“立即导出”。
