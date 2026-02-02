# dot

## 目的
将 DalamudACT 的 **DoT 伤害统计**尽可能与 **ACT（ACT.DieMoe + FFXIV_ACT_Plugin）**保持一致，并提供可重复的离线对照数据，便于持续战斗时“边打边采样、事后核对”。

## 当前结论（截至 2026-01-29）
- **展示口径**：在 `EnableActMcpSync=true` + `PreferActMcpTotals=true` 时，UI/导出的“总伤害、DPS/ENCDPS、DoT 总量/占比”优先采用 **ACT MCP 的 combatant 快照**（作为权威来源），用于规避本地统计在复杂场景下的漏算/错归因。
- **未完成**：本地 DoT 捕获/归因仍可能与 ACT 不一致，典型现象包括：
  - 同职业 DoT 伤害“有人异常高、有人为 0”，与 ACT 的 DoT 占比不符
  - `battle-stats.jsonl` 中同一 `pairId` 的 `local` 与 `act_mcp` 行 `totalDotDamage` 差异明显
- **高概率根因（待验证）**：`dot-debug.log` 中长期出现 `Network=0`（仅 `LegacyActorControlSelf` 入队），提示“网络侧 DoT tick 捕获路径未生效/未识别”，导致大量 `localDot=0` 或来源缺失。

## 已实现能力（用于对齐/采样）

### 1) ACT MCP 对齐（以 ACT 为准）
- 默认 Pipe：`act-diemoe-mcp`
- 关键配置：
  - `EnableActMcpSync=true`
  - `PreferActMcpTotals=true`
- 说明：当 ACT 快照可用时，DalamudACT 会将其写入 `ACTBattle.ActEncounter`，并在 UI/导出中优先使用 ACT 的 `damage/encdps/dps/dotDamage`。

### 2) 战斗中不重置（避免转阶段误分段）
- 修复点：当仍处于 `InCombat=true` 时，即使 ACT `endTime` 长时间不更新，也**不清空** `ActEncounter`，避免 Boss 转阶段/不可打导致 UI 回退本地口径、历史记录被空白覆盖。
- 位置：`DalamudACT/ACT2.cs`

### 3) DoT 诊断与自动导出
- 战斗结束自动导出：
  - `battle-stats.jsonl`：对照快照（`local` + `act_mcp`，同 `pairId`）
  - `dot-debug.log`：`DoTStats + DotDump(all)`（DoT tick 事件抽样）
- 关键配置：
  - `EnableEnhancedDotCapture=true`
  - `EnableActLikeAttribution=true`
  - `EnableDotDiagnostics=true`
  - `AutoExportDotDumpOnEnd=true`
  - `AutoExportDotDumpMax=200`

## 对照数据位置（SSOT：离线核对入口）
- 配置文件：
  - `%APPDATA%\\XIVLauncherCN\\pluginConfigs\\DalamudACT.json`
- 采样/对照日志：
  - `%APPDATA%\\XIVLauncherCN\\pluginConfigs\\DalamudACT\\battle-stats.jsonl`
  - `%APPDATA%\\XIVLauncherCN\\pluginConfigs\\DalamudACT\\dot-debug.log`

## 常用调试命令（游戏内）
- `/act stats [local|act|both] [log|file] [N]`：导出战斗统计快照（建议 `both file`）
- `/act dotstats [log|file]`：输出 DoT 采集统计（看 `Network=0`/UnknownSource/推断次数）
- `/act dotdump [log|file] [all] [N]`：输出 DoT tick 事件（定位 buffId/sourceId/被丢弃原因）
- `/act actdll [init]`：探测加载 ACT/FFXIV_ACT_Plugin DLL（实验/默认关闭；不建议在生产环境开启）

## 近期样本（用于快速回归验证）
> 样本以 `battle-stats.jsonl` 的 `pairId` 为主键；同一 `pairId` 会有 `source=local` 与 `source=act_mcp` 两行。

- 例：`pairId=bc0c9b2c081346eebcae4b514121cfe4`
  - `local.totalDotDamage=35967`
  - `act_mcp.totalDotDamage=20530`
- 例：`pairId=cc1e1829de8c4863ba8fe833deed4d69`
  - 历史样本曾出现 `local.totalDotDamage≈2997485` vs `act_mcp.totalDotDamage≈2786924`
  - 同时存在“单人 localDot 极大但 actDot 极小/为 0”与“localDot=0 但 actDot>0”的混合差异

## 下一步（下次继续时的最短路径）
1) **确认 ACT 侧 DoT 列/口径取值正确**
   - 用 ACT MCP 在 ACT 日志输出指定 combatant 的 `Items/ColCollection`（含 `dot` 关键词），确保 `dotDamage` 不是取错列导致“actDot 极小”。
2) **定位为何 `Network=0`**
   - 观察 `dot-debug.log` 的 `DoTStats 入队 …（Legacy …, Network 0）` 是否稳定复现。
   - 若稳定为 0，则需要补齐/修复网络侧 DoT tick 捕获路径（可能是识别条件不足或事件源未接入）。
3) **在修复本地 DoT 前继续“以 ACT 为准”**
   - 保持 `PreferActMcpTotals=true`，确保展示/导出与 ACT 一致，避免阶段/不可打导致误分段与历史空白覆盖。

## 风险备注：直接引用/复用 ACT DLL
- 在 Dalamud 进程内直接加载并执行 ACT/FFXIV_ACT_Plugin DLL 存在稳定性/兼容性/授权边界等风险（进程注入式依赖、AppDomain/依赖缺失、版本耦合）。
- 当前仅保留“探测 PoC”入口（默认关闭），不作为主路径。

