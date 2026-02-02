# ui

## 目的
提供战斗统计的可视化窗口与配置入口，确保用户可选择与 ACT 一致的展示口径。

## 模块概述
- **职责**：Cards/Summary/Launcher 窗口渲染、排序与筛选、配置 UI
- **状态**：稳定
- **最后更新**：2026-01-29

## 关键设计

### DPS 口径切换（对齐 ACT）
- 提供 `DPS 口径` 下拉：
  - **ENCDPS（按战斗时长）**：与 ACT Encounter/EncDPS 口径一致，适合对照
  - **DPS（按个人活跃时长）**：以个人首末次造成伤害的时间窗计算
- 默认口径：**ENCDPS（按战斗时长）**
- 说明：当选择 **DPS（个人活跃）** 时，单人 DPS 的求和通常不会等于“总秒伤”（总秒伤按战斗时长），属于口径差异而非统计错误

### DoT 展示
- 若 DoT 可分配：通过 `DotDmgList` 将未知来源 DoT 分配到玩家总伤害中
- Tooltip 的 **DOT伤害**：
  - 当启用 `PreferActMcpTotals` 且 ACT 快照可用时：优先展示 ACT 的 `dotDamage`（并显示 DOT 占比），不再回退本地 tick/模拟（避免两套口径混用导致“DOT 比例对不上”）
  - 否则：以 `DotDamageByActor`（tick 统计）为主，并在存在“未知来源 DoT”分配时叠加模拟补正（`DotDmgList`）
- 当启用 `PreferActMcpTotals` 且 ACT 快照可用时：窗口顶部/汇总的 **DOT 总伤害** 同样使用 ACT combatants 的 `dotDamage` 求和（避免显示为 0）
- 当存在 `TotalOnly`（未能识别 skill/status）的 tick 时，会额外显示“未识别DOT tick”，便于定位 DoT 归因缺口
- 设置窗口 → DoT：提供按钮将 `DoTStats`/`DotDump` 写入 `dalamud.log` 或导出到 `pluginConfigs/DalamudACT/dot-debug.log`，便于离线排查偏差

### 对齐/导出（对照 ACT）
- 设置窗口 → 对齐/导出：
  - `AutoExportBattleStatsOnEnd`：战斗结束自动导出 `battle-stats.jsonl`
  - `AutoExportDotDumpOnEnd`：战斗结束自动导出 `DoTStats` + `DotDump(all)` 到 `dot-debug.log`（便于离线核对 DoT 归因差异）
  - `AutoExportDotDumpMax`：DotDump 最大输出条数（默认 200；范围 20~2000）
  - `EncounterTimeoutMs`：遭遇结束超时（毫秒）。过短会在转阶段/脱战瞬间误分段；建议 10s~30s
  - “立即导出: BattleStats(JSONL)”：手动触发一次快照写入（用于对齐采样）
  - `EnableActMcpSync`：启用 ACT MCP 同步（Named Pipe，配置 v21 起默认启用）
  - `PreferActMcpTotals`：当 ACT 快照可用时，UI/手动导出优先使用 ACT 的总伤害与 ENCDPS/DPS 口径；同时会同步遭遇计时以对齐 ACT（减少误分段）
  - 自动导出：启用 `EnableActMcpSync` 时默认导出 `both`（`local` + `act_mcp`，同一个 `pairId`），便于离线脚本对比
  - `ActMcpPipeName`：Pipe 名称（默认 `act-diemoe-mcp`，对应 ACT.McpPlugin）
  - **实验/高风险**：`EnableActDllBridgeExperimental` + `ActDieMoeRoot`：在游戏进程内尝试加载 ACT/`FFXIV_ACT_Plugin` DLL（仅 PoC 探测“是否能加载/失败原因”，不保证可运行或可复用统计逻辑）
    - UI 按钮：`探测加载` / `尝试 InitPlugin`
    - 命令：`/act actdll`（仅探测）与 `/act actdll init`（尝试 InitPlugin）
  - 命令行导出：`/act stats local|act|both ...` 可显式选择导出源（对齐调试时推荐 `both`）

### 窗口交互
- Alt + 拖拽调整窗口位置（避免误触）
- Summary 支持缩放与自定义背景色/透明度

### 历史战斗浏览
- 仅在 **非战斗状态** 下允许翻阅历史（`<<`/`>>`/`最新`）
- 历史列表会排除“无任何统计数据”的空白战斗（无玩家伤害/无 LB/无 ACT 快照；仅未知来源 DoT 的时间片也会被丢弃），避免空白时间片覆盖真实战斗记录

## 关联模块
- battle
- core

## 历史记录
- [202601101832_summary_window_drag_style](../../history/2026-01/202601101832_summary_window_drag_style/) - Alt 拖拽窗口交互
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 增加 ENCDPS/DPS 口径切换
- [202601220445_act_encounter_sync](../../history/2026-01/202601220445_act_encounter_sync/) - 对齐/导出：BattleStats 自动导出与设置项
- [202601290237_dot_stats_skill_based](../../history/2026-01/202601290237_dot_stats_skill_based/) - DOT 统计口径改为技能明细汇总（含未识别 tick 提示）
- [202601290301_dot_tick_source_guard](../../history/2026-01/202601290301_dot_tick_source_guard/) - DoT tick 防御：目标无该来源 DoT 时回退未知来源（避免错归因）
- [202601290336_dot_history_act_stale_fix](../../history/2026-01/202601290336_dot_history_act_stale_fix/) - 历史战斗空白覆盖修复 + DoT 归因更保守
- [202601291554_dot_align_act_v3](../../history/2026-01/202601291554_dot_align_act_v3/) - ACT 快照模式 DOT 总量显示 + DoT 推断不覆盖可信来源
