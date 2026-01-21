# 方案任务: DoT tick 同通道双事件去重修复

路径: `helloagents/plan/202601210310_dot_tick_dedup_fix/`

---

## 1. 问题定位
- [√] 1.1 对照 ACT 的 DoT 统计（Simulated DoTs）确认偏差存在
- [√] 1.2 排查 DoT tick 采集链路（ActorControlSelf → DotEventCapture → ACTBattle/UI）
- [√] 1.3 识别同通道可能存在 `buffId=0` 与 `buffId!=0` 的双事件，导致 DoT 统计翻倍

## 2. 代码修复
- [√] 2.1 `DotEventCapture` 扩展去重缓存记录 `buffId`，补齐“同通道 buffId 0/非0 双事件”去重
- [√] 2.2 `DotEventCapture.FlushInto` 批处理排序：同毫秒优先处理带 `buffId` 的事件
- [√] 2.3 增补 `/act dotstats`，输出 DoT 采集统计，便于通过卫月 MCP 拉取核对
- [√] 2.4 `/act dotstats` 增加自己 DoT 的 Tick/模拟/合计输出，便于直接对照 ACT 的 Simulated DoTs
- [√] 2.5 新增 `/act dotdump [all] [N]`，输出最近 DoT tick 事件（含去重/归因），用于定位错归因/重复统计
- [√] 2.6 默认 DoT 来源推断：未开启 ACTLike 时也优先扫描目标 `StatusList`，失败再回退缓存，减少缓存过期导致的错归因
- [√] 2.7 去重判定改用原始 `buffId`（推断补齐不影响判重），避免同 tick 双事件仍被重复计入
- [√] 2.8 跨通道去重增加 `buffId` 不一致保护：当两侧 `buffId` 可确定且明确不一致时跳过去重，避免多 DoT 场景同伤害误删

## 3. 验证
- [√] 3.1 `dotnet build DalamudACT.sln -c Debug`
- [√] 3.2 `dotnet build DalamudACT.sln -c Release`
- [√] 3.3 卫月 MCP 调用 `/act dotstats`，确认插件可输出统计信息
- [√] 3.4 卫月 MCP 调用 `/xlplugins reload DalamudACT` + `/act dotstats`，确认重载后 DoT 统计仍可用
- [√] 3.5 `dotnet build DalamudACT.sln -c Debug`（dedup 逻辑更新后回归编译）

## 4. 文档同步
- [√] 4.1 更新 `helloagents/wiki/modules/battle.md`
- [√] 4.2 更新 `helloagents/wiki/modules/core.md`
- [√] 4.3 更新 `helloagents/CHANGELOG.md`
