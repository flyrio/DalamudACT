# 方案任务: DotPot 从 ff14mcp 同步

路径: `helloagents/plan/202601141010_dot_sync_ff14mcp/`

---

## 1. PotencyUpdater 扩展
- [√] 1.1 增加 `--ff14mcp-dots` 参数，读取 ff14mcp 的 `dots_by_job.json`
- [√] 1.2 处理同一 status 多 potency 冲突（按出现次数/等级选择，并输出 warnings）
- [√] 1.3 合并现有 DotPot（ff14mcp 覆盖，缺失条目保留）

## 2. 数据同步
- [√] 2.1 运行 PotencyUpdater 更新 `DalamudACT/Potency.cs` 的 DotPot 段（补齐缺失 DoT 状态与威力）

## 3. 验证
- [√] 3.1 `dotnet build DalamudACT.sln -c Release`

## 4. 文档同步
- [√] 4.1 更新 `helloagents/wiki/modules/potency.md`
- [√] 4.2 更新 `helloagents/wiki/modules/battle.md`
- [√] 4.3 更新 `helloagents/CHANGELOG.md`
- [√] 4.4 更新 `helloagents/history/index.md`
