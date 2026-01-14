# 任务清单: 7.4 DoT 数据集同步与口径对齐

目录: `helloagents/plan/202601150539_dot_catalog_sync_74/`

---

## 1. ff14-mcp 数据集
- [√] 1.1 使用 Chrome MCP 校验 7.4 来源并记录更新时间，更新 `C:/Users/10576/ff14-mcp/data/sources.json`，验证 why.md#需求-74-dot-数据集更新-场景-版本与等级校验
- [-] 1.2 如来源内容变化，更新 `C:/Users/10576/ff14-mcp/data/skills_tooltips.md`，验证 why.md#需求-74-dot-数据集更新-场景-版本与等级校验
> 备注: 来源内容未发现变更，本次未更新 skills_tooltips.md
- [√] 1.3 运行 `C:/Users/10576/ff14-mcp/scripts/generate_dot_catalog.py` 生成 `C:/Users/10576/ff14-mcp/data/dots_by_job.json` 与 `C:/Users/10576/ff14-mcp/data/dots_by_job.md`，验证 why.md#需求-74-dot-数据集更新-场景-dot-数据集全量更新，依赖任务1.1

## 2. potency 同步
- [√] 2.1 使用 PotencyUpdater 同步 `DalamudACT/Potency.cs` DotPot，验证 why.md#需求-插件-dot-威力同步-场景-dotpot-同步与覆盖，依赖任务1.3
- [-] 2.2 如存在缺失状态，更新 `tools/PotencyUpdater/Program.cs` 的 DotPotencyOverrides，验证 why.md#需求-插件-dot-威力同步-场景-dotpot-同步与覆盖，依赖任务2.1
> 备注: 未发现缺失状态；同一 StatusId 多威力冲突沿用生成结果

## 3. 安全检查
- [√] 3.1 执行安全检查（按G9: 输入验证、敏感信息处理、权限控制、EHRB风险规避）

## 4. 文档更新
- [√] 4.1 更新 `helloagents/wiki/modules/potency.md` 记录数据来源与同步流程
- [√] 4.2 更新 `helloagents/CHANGELOG.md` 记录数据更新

## 5. 测试
- [√] 5.1 复核 `dots_by_job.json` 与 `dots_by_job.md` 的职业覆盖与状态数量，验证 why.md#需求-74-dot-数据集更新-场景-dot-数据集全量更新
