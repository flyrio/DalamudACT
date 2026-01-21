# 技术设计: 修复伤害统计不准确（高伤害值解析）

## 技术方案

### 核心技术
- .NET / Dalamud Hook
- `FFXIVClientStructs` 的 `ActionEffectHandler` 结构体解析

### 实现要点
- 在 `ACT2.Ability` 解析伤害时，新增一个明确的解码函数（或局部逻辑）用于从 `ActionEffectHandler.Effect` 还原 24-bit 伤害：
  - `damage = (uint)effect.Value | ((uint)effect.Param3 << 16)`
- 仅对伤害类 effect（`Type` 为 Damage/Blocked/Parried）应用该解码规则。
- 保持 `crit/direct` 标记解析与事件归档逻辑不变，避免扩大变更面。

## 安全与性能
- **安全:** 仅本地内存解析与统计，不涉及外部网络写入，不启用危险 MCP 工具。
- **性能:** 单次解码为常量时间位运算，对帧更新影响可忽略。

## 测试与部署
- **测试:** `dotnet build`，确保编译通过；进游戏使用日志窗口观察包含 >65535 的命中，确认插件统计不再截断。
- **部署:** 打包/热加载插件后在实际战斗场景验证。
