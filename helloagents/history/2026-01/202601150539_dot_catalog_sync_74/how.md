# 技术设计: 7.4 DoT 数据集同步与口径对齐

## 技术方案
### 核心技术
- Chrome MCP: 查阅 7.4 DoT 状态与威力数据的权威来源
- ff14-mcp 数据生成: `C:/Users/10576/ff14-mcp/scripts/generate_dot_catalog.py`
- PotencyUpdater: `tools/PotencyUpdater/Program.cs`

### 实现要点
- 通过 Chrome MCP 验证 7.4 版本与等级上限 100 的 DoT 数据口径
- 必要时更新 ff14-mcp 的原始来源数据（如 `data/skills_tooltips.md`）
- 运行生成脚本更新 `dots_by_job.json` 与 `dots_by_job.md`
- 更新 `data/sources.json` 记录来源与更新时间
- 使用 PotencyUpdater 同步 `DalamudACT/Potency.cs` 的 DotPot
- 如存在缺失状态，补充 `DotPotencyOverrides` 覆盖项

## 架构设计
无架构变更。

## API设计
无。

## 数据模型
无结构变更，仅更新数据内容。

## 安全与性能
- **安全:** 仅访问公开数据源，不引入密钥或敏感信息；避免连接未授权环境
- **性能:** 生成脚本与同步工具为离线运行，不影响运行时性能

## 测试与部署
- **测试:** 运行生成脚本与 PotencyUpdater；对比输出文件更新时间与数据覆盖情况
- **部署:** 无需额外部署步骤，提交并推送代码与数据更新
