# 任务清单: ACTLike DoT/召唤物归因（备选）

## 1. 配置与 UI
- [√] 新增配置项：`EnableActLikeAttribution`（默认关闭）
- [√] 设置界面增加开关与说明

## 2. 召唤物伤害合并增强
- [√] 增加 OwnerCache 预热（节流扫描 ObjectTable）
- [√] Ability 解析在 Owner 解析失败时预热并重试，减少事件丢弃

## 3. DoT 归因新逻辑（ACTLike）
- [√] 新增 `TryResolveDotSourceByDamage(targetId, buffId, damage)`（同 statusId 多来源时按伤害匹配）
- [√] Dot tick 处理链路按“来源优先级 + 伤害匹配 + 兜底模拟”改造（仅在备选模式启用时生效）
- [√] （可选）当 `sourceId/buffId` 同时缺失时，尝试按伤害推断 pair（严格阈值）

## 4. 质量验证
- [√] `dotnet build` / `dotnet test`（如存在测试）
- [?] 在游戏内用 DoT 诊断计数 + 与 ACT 对比进行 A/B 验证（手工）
  > 备注: 需要在游戏内验证（本环境无法执行），建议开启 DoT 区的诊断计数与 `/act dotstats` 对比 ACT。

## 5. 文档与收尾
- [√] 更新知识库：`helloagents/wiki/modules/battle.md`（补充备选模式说明）
- [√] 更新 `helloagents/CHANGELOG.md`
- [√] 迁移方案包至 `helloagents/history/2026-01/` 并更新 `helloagents/history/index.md`
