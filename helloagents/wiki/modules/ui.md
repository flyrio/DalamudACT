# ui

## 目的
提供战斗统计的可视化窗口与配置入口，确保用户可选择与 ACT 一致的展示口径。

## 模块概述
- **职责**：Cards/Summary/Launcher 窗口渲染、排序与筛选、配置 UI
- **状态**：稳定
- **最后更新**：2026-01-12

## 关键设计

### DPS 口径切换（对齐 ACT）
- 提供 `DPS 口径` 下拉：
  - **ENCDPS（按战斗时长）**：与 ACT Encounter/EncDPS 口径一致，适合对照
  - **DPS（按个人活跃时长）**：以个人首末次造成伤害的时间窗计算
- 说明：当选择 **DPS（个人活跃）** 时，单人 DPS 的求和通常不会等于“总秒伤”（总秒伤按战斗时长），属于口径差异而非统计错误

### DoT 展示
- 若 DoT 可分配：通过 `DotDmgList` 将未知来源 DoT 分配到玩家总伤害中
- Tooltip 显示 DoT tick/分配结果，便于核对

### 窗口交互
- Alt + 拖拽调整窗口位置（避免误触）
- Summary 支持缩放与自定义背景色/透明度

## 关联模块
- battle
- core

## 历史记录
- [202601101832_summary_window_drag_style](../../history/2026-01/202601101832_summary_window_drag_style/) - Alt 拖拽窗口交互
- [202601120554_act_damage_sync](../../history/2026-01/202601120554_act_damage_sync/) - 增加 ENCDPS/DPS 口径切换

