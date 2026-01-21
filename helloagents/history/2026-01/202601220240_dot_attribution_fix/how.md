# 技术设计: DoT 归因偏差修复

## 技术方案

### 核心技术
- .NET / Dalamud Hook（ActorControlSelf 捕获 DoT tick）
- 目标状态表（`ObjectTable.SearchByEntityId(targetId)` → `StatusList`）用于 `buffId`/`sourceId` 推断

### 实现要点
1. **推断顺序调整**
   - 当 `buffId==0` 且启用增强归因时：先尝试补齐 `buffId`（缓存 → 目标状态唯一匹配 → 按伤害匹配 `buffId`）。
   - 补齐 `buffId` 后，若 `sourceId` 仍未知：再尝试基于 `StatusList` 做**唯一来源**推断（允许多来源并存时拒绝推断）。

2. **移除“同时缺失 sourceId/buffId 时按伤害推断来源”**
   - 现状：`EnableActLikeAttribution` + `EnableEnhancedDotCapture` 场景下会调用 `TryResolveDotPairByDamage` 推断来源，复杂场景易错归因。
   - 调整：保留“仅推断 buffId 用于模拟分配”的能力；来源仅在 `StatusList` 可唯一确定时推断，避免扩大错归因。

3. **保留去重与诊断**
   - 不改变去重窗口与 key 策略，避免引入新的重复统计风险。
   - 继续通过 `/act dotstats`、`/act dotdump` 验证：未知来源计数应下降或保持；错归因（主观）应显著减少。

## 安全与性能
- **安全:** 无外部服务调用，无敏感信息处理，不引入高风险操作。
- **性能:** 仅在 `buffId==0` 或 `sourceId` 需推断时扫描 `StatusList`，且列表规模有限；保持现有缓存与窗口去重策略，避免频繁分配。

## 测试与验证
- **构建验证:** `dotnet build DalamudACT.sln -c Release`。
- **运行验证（人工）:**
  - 开启 `EnableDotDiagnostics`，对比修复前后 `/act dotstats` 的 UnknownSource/推断计数变化。
  - 在多玩家/多 DoT 场景下观察：个人 DoT 归属与总伤害与 ACT 差异收敛。

