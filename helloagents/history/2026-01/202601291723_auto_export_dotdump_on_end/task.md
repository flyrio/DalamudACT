# task

> 轻量迭代：战斗结束自动导出 DoT 采样（DotDump/DoTStats）用于离线核对

## 任务清单

- [√] 调整：战斗结束不再在 `CheckTime` 内立即清空 DoT 缓存，改为在 `Update` 中导出后再清空
- [√] 新增：配置 `AutoExportDotDumpOnEnd`/`AutoExportDotDumpMax`（升级到 v22 默认开启）
- [√] UI：设置窗口 → 对齐/导出 增加开关与最大条数输入
- [√] 文档：更新 battle/ui 模块说明与 Changelog
- [√] 构建：`dotnet build DalamudACT.sln -c Release` 通过

## 备注

- 输出位置：`%APPDATA%\\XIVLauncherCN\\pluginConfigs\\DalamudACT\\dot-debug.log`
- 写入内容：`DoTStats` 统计 + `DotDump(all)` 最近 N 条（默认 200）
