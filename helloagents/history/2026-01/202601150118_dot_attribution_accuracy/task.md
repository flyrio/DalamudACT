# 任务清单: DoT 归因准确性修复

目录: `helloagents/plan/202601150118_dot_attribution_accuracy/`

---

## 1. 采集与归因逻辑
- [√] 1.1 在 `DalamudACT/DotEventCapture.cs` 中调整去重与未知来源 buff 推断流程，验证 why.md#需求-dot-归因准确性-场景-多人同职业多目标 与 why.md#需求-dot-归因准确性-场景-buffid0来源未知
- [√] 1.2 在 `DalamudACT/Struct/ACTBattle.cs` 中新增不依赖来源的 buff 推断与伤害匹配方法，验证 why.md#需求-dot-归因准确性-场景-buffid0来源未知

## 2. DoT 威力表同步
- [-] 2.1 通过 PotencyUpdater 同步 `DalamudACT/Potency.cs` 的 DotPot，并在 `tools/PotencyUpdater/Program.cs` 添加缺失状态覆盖，验证 why.md#需求-dot-威力表补齐-场景-目标状态含未收录-dot
  > 备注: 未提供 ff14mcp 数据路径，未执行 DotPot 同步；仅新增覆盖入口，待补齐数据后执行。

## 3. 安全检查
- [√] 3.1 执行安全检查（按G9: 输入验证、敏感信息处理、权限控制、EHRB风险规避）

## 4. 文档更新
- [√] 4.1 更新 `helloagents/wiki/modules/battle.md`
- [√] 4.2 更新 `helloagents/wiki/modules/potency.md`
- [√] 4.3 更新 `helloagents/CHANGELOG.md`

## 5. 测试
- [-] 5.1 游戏内验证：开启 DoT 诊断日志/计数，对比多目标同职业场景下主列表/汇总/Tooltip 的 DoT 一致性与未知/推断计数变化
  > 备注: 需要在游戏内手动验证。
