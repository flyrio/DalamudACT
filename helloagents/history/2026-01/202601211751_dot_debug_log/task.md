# 方案任务: DoT 调试输出改为卫月日志/文件

路径: `helloagents/plan/202601211751_dot_debug_log/`

---

## 1. 需求确认
- [√] 1.1 DoT 调试信息优先写入 `dalamud.log`（Info 级），减少聊天栏依赖
- [√] 1.2 支持可选导出到 `pluginConfigs/DalamudACT/dot-debug.log`，便于离线分析

## 2. 开发实现
- [√] 2.1 扩展 `/act dotstats`：新增 `log`/`file` 输出模式（不再打印聊天栏正文）
- [√] 2.2 扩展 `/act dotdump [all] [N]`：新增 `log`/`file` 输出模式（支持 `dotdump log all 20`）
- [√] 2.3 增加配置窗口 DoT 区域按钮：一键写入日志/导出文件
- [√] 2.4 文件导出加入基础轮转/截断策略，避免无限增长

## 3. 验证
- [√] 3.1 `dotnet build DalamudACT.sln -c Debug`
- [√] 3.2 `dotnet build DalamudACT.sln -c Release`

## 4. 文档同步
- [√] 4.1 更新 `helloagents/wiki/modules/battle.md`：补充日志/文件输出模式说明
- [√] 4.2 更新 `helloagents/CHANGELOG.md`
