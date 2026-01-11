# potency

## 目的
维护技能与 DoT 的 potency 数据。

## 模块概述
- **职责:** Potency 表维护与映射
- **状态:** ✅稳定
- **最后更新:** 2026-01-09

## 规范

### 需求: Potency 数据准确性
**模块:** potency
保持 Potency 表与版本一致，确保 DoT 估算可信。

#### 场景: 版本更新
- 提供新版本的 SkillPot/DotPot
- 避免旧版本数据干扰统计
- DotPot/BuffToAction 缺失时需运行 PotencyUpdater 生成

## API接口
无

## 数据模型
无

## 依赖
- core
- battle

## 变更历史
- [202601092006_dot_damage_accuracy](../../history/2026-01/202601092006_dot_damage_accuracy/) - Potency 维护流程说明补充
