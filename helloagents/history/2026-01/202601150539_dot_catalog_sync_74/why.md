# 变更提案: 7.4 DoT 数据集同步与口径对齐

## 需求背景
当前需要以 7.4 版本（等级上限 100）为基准，完整更新 ff14-mcp 的 DoT 数据集，并同步插件的 DoT 威力表，确保 DoT 归因与推断在新版本中可靠。

## 变更内容
1. 使用 Chrome 查阅权威来源，确认 7.4 DoT 技能与状态数据。
2. 生成并更新 ff14-mcp 的 `dots_by_job.json` 与 `dots_by_job.md`。
3. 同步 `DalamudACT/Potency.cs` 的 DotPot，并补充必要覆盖项。
4. 记录数据来源更新时间，确保 7.4 时间有效性可追溯。

## 影响范围
- **模块:** ff14-mcp 数据集、potency
- **文件:**
  - `C:/Users/10576/ff14-mcp/data/dots_by_job.json`
  - `C:/Users/10576/ff14-mcp/data/dots_by_job.md`
  - `C:/Users/10576/ff14-mcp/data/sources.json`
  - `tools/PotencyUpdater/Program.cs`
  - `DalamudACT/Potency.cs`
  - `helloagents/wiki/modules/potency.md`
  - `helloagents/CHANGELOG.md`
- **API:** 无
- **数据:** DoT 数据集/威力表

## 核心场景

### 需求: 7.4 DoT 数据集更新
**模块:** ff14-mcp

#### 场景: 版本与等级校验
确认数据来源与内容符合 7.4、等级上限 100。
- 预期结果: 数据来源记录更新时间，DoT 数据集与版本一致。

#### 场景: DoT 数据集全量更新
基于来源生成 `dots_by_job.json` 与 `dots_by_job.md`。
- 预期结果: 数据集完整覆盖战斗职业 DoT 技能与状态。

### 需求: 插件 DoT 威力同步
**模块:** potency

#### 场景: DotPot 同步与覆盖
由 ff14-mcp 数据集同步 `Potency.cs`，补齐缺失状态。
- 预期结果: DotPot 与 7.4 数据一致，缺失状态覆盖可追溯。

## 风险评估
- **风险:** 数据来源差异导致误差
  - **缓解:** 记录来源更新时间并做最小必要覆盖
- **风险:** 数据集更新影响推断口径
  - **缓解:** 同步更新 Potency 与文档，保持一致性
