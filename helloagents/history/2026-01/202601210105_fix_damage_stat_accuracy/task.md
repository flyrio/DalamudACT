# 任务清单: 修复伤害统计不准确（高伤害值解析）

目录: `helloagents/plan/202601210105_fix_damage_stat_accuracy/`

---

## 1. core
- [√] 1.1 在 `DalamudACT/ACT2.cs` 中修复 `ActionEffectHandler.Effect` 的伤害值解码逻辑，验证 why.md#需求-高伤害值统计准确-场景-单次伤害超过-65535

## 2. 安全检查
- [√] 2.1 执行安全检查（按G9: 不引入危险命令/敏感信息/外部写入）

## 3. 文档更新
- [√] 3.1 更新 `helloagents/wiki/modules/core.md`，同步最新伤害值解码口径
- [√] 3.2 更新 `helloagents/CHANGELOG.md`（Unreleased）记录本次修复

## 4. 测试
- [√] 4.1 执行 `dotnet build`，确保项目可编译
