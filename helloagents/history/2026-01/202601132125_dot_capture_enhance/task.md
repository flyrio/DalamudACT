# 任务清单: dot_capture_enhance

目录: `helloagents/plan/202601132125_dot_capture_enhance/`

---

## 1. DoT 采集链路（core/battle）
- [√] 1.1 在 `DalamudACT/ACT2.cs` 与新增的 `DalamudACT/DotEventCapture.cs` 中实现 `DotTickEvent` 统一入口与基础接线
- [-] 1.2 新增“网络层 DoT 采集入口”（原计划使用 `IGameNetwork`）- 由于 SDK 标记 `IGameNetwork` 为 Obsolete(error)，本次未实现；改为在 legacy 路径做增强归因与技能明细
- [√] 1.3 在 `DalamudACT/DotEventCapture.cs` 与 `DalamudACT/Struct/ACTBattle.cs` 中加入去重与字段修复策略（含 buffId=0 / source=E0000000 兜底）
- [√] 1.4 在 `DalamudACT/Struct/ACTBattle.cs` 中补强 DoT 归因缓存/推断的边界处理（仅唯一匹配推断）

## 2. 配置与可观测性（ui）
- [√] 2.1 在 `DalamudACT/Configuration.cs` 中新增配置项：启用 DoT 增强归因（buffId 推断/技能明细）、启用 DoT 诊断统计/日志
- [√] 2.2 在 `DalamudACT/PluginUI.cs` 中增加 UI 开关与诊断展示（入队/处理/去重/未知来源/推断次数）

## 3. 安全检查
- [√] 3.1 执行安全检查（按G9: 输入验证、敏感信息处理、权限控制、EHRB风险规避）

## 4. 文档更新
- [√] 4.1 更新 `helloagents/wiki/modules/battle.md`（补充 DoT tick 管线、唯一匹配推断、诊断口径说明）
- [√] 4.2 更新 `helloagents/CHANGELOG.md`（记录 DoT 统计修复与验证方式）

## 5. 测试与验证
- [√] 5.1 执行构建验证：`dotnet build DalamudACT.sln -c Release`
- [?] 5.2 游戏内验证：本地学者/队友白魔 DoT tick 统计随 tick 增长；开启诊断后确认推断与去重正常
- [?] 5.3 对齐验证（建议）：同一场战斗对比 ACT 的 DoT 与总伤害，确认不再系统性偏低
