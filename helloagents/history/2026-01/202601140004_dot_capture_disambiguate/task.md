# 方案任务: DoT tick 归因补强（v2）

路径: `helloagents/plan/202601140004_dot_capture_disambiguate/`

---

## 1. 归因修正
- [√] 1.1 补齐 DoT 威力表缺失的 `statusId=1200`
- [√] 1.2 来源已知且同源多 DoT 时，按 tick 伤害 + DPP + 威力推断 buffId（`TryResolveDotBuffByDamage`）
- [√] 1.3 `sourceId`/`buffId` 均缺失时，按 tick 伤害 + DPP + 威力匹配 `(source,status)`（`TryResolveDotPairByDamage`）

## 2. DoT 分配连续性
- [√] 2.1 `CalcDot` 引入 DPP fallback，避免 DPP 不就绪导致分配为 0

## 3. 验证
- [√] 3.1 `dotnet build DalamudACT.sln -c Release`

## 4. 文档同步
- [√] 4.1 更新 `helloagents/wiki/modules/battle.md`
- [√] 4.2 更新 `helloagents/CHANGELOG.md`
