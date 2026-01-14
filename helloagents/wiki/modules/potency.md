# potency

## 目的
维护技能与 DoT 的 potency 数据。

## 模块概述
- **职责:** Potency 表维护与映射
- **状态:** ✅稳定
- **最后更新:** 2026-01-15

## 规范

### 需求: Potency 数据准确性
**模块:** potency
保持 Potency 表与版本一致，确保 DoT 估算可信。

#### 场景: 版本更新
- 提供新版本的 SkillPot/DotPot
- 避免旧版本数据干扰统计
- DotPot 同步：运行 PotencyUpdater 的 `--ff14mcp-dots` 从 ff14mcp 的 `dots_by_job.json` 更新 DotPot（无需访问 XIVAPI）
- DoT 数据来源刷新：同步更新 `ff14-mcp/data/dots_by_job.json`、`ff14-mcp/data/dots_by_job.md` 与 `ff14-mcp/data/sources.json`（记录抓取日期）
- 若发现 buffId 推断失败或状态缺失，优先对照 ff14mcp DoT 数据并补齐 PotencyUpdater 覆盖项
- SkillPot 更新：仍使用默认模式（需要 `--game`，并从 XIVAPI 拉取英文描述解析）

## API接口
无

## 数据模型
无

## 依赖
- core
- battle

## 变更历史
- [202601092006_dot_damage_accuracy](../../history/2026-01/202601092006_dot_damage_accuracy/) - Potency 维护流程说明补充
- [202601140004_dot_capture_disambiguate](../../history/2026-01/202601140004_dot_capture_disambiguate/) - DotPot 补齐 1200（诗人双 DoT）
- [202601141010_dot_sync_ff14mcp](../../history/2026-01/202601141010_dot_sync_ff14mcp/) - DotPot 改为支持从 ff14mcp 同步（补齐缺失 DoT 状态与威力）
- [202601150539_dot_catalog_sync_74](../../history/2026-01/202601150539_dot_catalog_sync_74/) - 7.4 DoT 数据集与 DotPot 同步
