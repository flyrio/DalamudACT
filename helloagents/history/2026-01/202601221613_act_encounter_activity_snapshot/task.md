# 任务清单: 对齐 ACT 遭遇计时与 BattleStats 快照稳定化

目录: `helloagents/plan/202601221613_act_encounter_activity_snapshot/`

---

## 1. battle（遭遇生命周期/导出）
- [√] 1.1 在 `DalamudACT/ACT2.cs` 的 `Ability(ActionEffect)` 中对 damage-like effect 调用 `MarkEncounterActivityOnly`，验证 why.md#需求-对齐-act-遭遇分段
- [√] 1.2 在 `DalamudACT/ACT2.cs` 的 BattleStats 导出中引入 `BattleStatsSnapshot`，避免并发枚举不一致，验证 why.md#需求-稳定对照采样
- [√] 1.3 `both` 模式要求 fresh 的 ACT 快照；`file` 模式缺失快照时写入 JSON 诊断行（不写非 JSON 文本），验证 why.md#需求-稳定对照采样

## 2. 安全检查
- [√] 2.1 检查无敏感信息写入/无新增外部网络调用（仅 Named Pipe 可选读取）

## 3. 文档更新
- [√] 3.1 更新 `helloagents/wiki/modules/battle.md`
- [√] 3.2 更新 `helloagents/CHANGELOG.md`

## 4. 构建与验证
- [√] 4.1 `dotnet build DalamudACT.sln -c Release`
- [√] 4.2 游戏内执行 `/act stats both file`，确认可生成可解析 JSONL（含错误诊断行）
