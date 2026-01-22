# ui

## 目的
提供战斗统计的可视化窗口与配置入口，确保用户可选择与 ACT 一致的展示口径。

## 模块概述
- **职责**：Cards/Summary/Launcher 窗口渲染、排序与筛选、配置 UI
- **状态**：稳定
- **最后更新**：2026-01-22

## 关键设计

### DPS 口径切换（对齐 ACT）
- 提供 `DPS 口径` 下拉：
  - **ENCDPS（按战斗时长）**：与 ACT Encounter/EncDPS 口径一致，适合对照
  - **DPS（按个人活跃时长）**：以个人首末次造成伤害的时间窗计算
- 默认口径：**ENCDPS（按战斗时长）**
- 说明：当选择 **DPS（个人活跃）** 时，单人 DPS 的求和通常不会等于“总秒伤”（总秒伤按战斗时长），属于口径差异而非统计错误

### DoT 展示
- 若 DoT 可分配：通过 `DotDmgList` 将未知来源 DoT 分配到玩家总伤害中
- Tooltip 显示 DoT tick/分配结果，便于核对
- 设置窗口 → DoT：提供按钮将 `DoTStats`/`DotDump` 写入 `dalamud.log` 或导出到 `pluginConfigs/DalamudACT/dot-debug.log`，便于离线排查偏差

### 对齐/导出（对照 ACT）
- 设置窗口 → 对齐/导出：
  - `AutoExportBattleStatsOnEnd`：战斗结束自动导出 `battle-stats.jsonl`
  - “立即导出: BattleStats(JSONL)”：手动触发一次快照写入（用于对齐采样）
  - `EnableActMcpSync`：启用 ACT MCP 同步（Named Pipe）
  - `PreferActMcpTotals`：当 ACT 快照可用时，UI/手动导出优先使用 ACT 的总伤害/ENCDPS 口径（自动导出仍固定为本地口径，便于脱离 ACT 运行）
  - `ActMcpPipeName`：Pipe 名称（默认 `act-diemoe-mcp`，对应 ACT.McpPlugin）
  - 命令行导出：`/act stats local|act|both ...` 可显式选择导出源（对齐调试时推荐 `both`）

### 窗口交互
- Alt + 拖拽调整窗口位置（避免误触）
- Summary 支持缩放与自定义背景色/透明度

## 关联模块
- battle
- core

## 历史记录
- [202601101832_summary_window_drag_style](../../history/2026-01/202601101832_summary_window_drag_style/) - Alt 拖拽窗口交互
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 增加 ENCDPS/DPS 口径切换
- [202601220445_act_encounter_sync](../../history/2026-01/202601220445_act_encounter_sync/) - 对齐/导出：BattleStats 自动导出与设置项
