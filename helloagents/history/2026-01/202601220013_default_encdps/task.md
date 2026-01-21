# 方案任务: 默认 DPS 口径调整为 ENCDPS

路径: `helloagents/plan/202601220013_default_encdps/`

---

## 1. 变更内容
- [√] 1.1 默认 `DpsTimeMode` 调整为 `0=ENCDPS（按战斗时长）`
- [√] 1.2 迁移/容错逻辑保持一致：旧版本缺失值与非法值回退到 ENCDPS

## 2. 验证
- [√] 2.1 `dotnet build DalamudACT.sln -c Debug`
- [√] 2.2 `dotnet build DalamudACT.sln -c Release`

## 3. 文档同步
- [√] 3.1 更新 `helloagents/wiki/modules/ui.md`
- [√] 3.2 更新 `helloagents/CHANGELOG.md`
