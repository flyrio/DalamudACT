# 任务清单: DoT 归因偏差修复

目录: `helloagents/plan/202601220240_dot_attribution_fix/`

---

## 1. DoT 采集与归因
- [√] 1.1 在 `DalamudACT/DotEventCapture.cs` 调整 DoT tick 推断顺序（先补齐 buffId，后补齐 sourceId），验证 why.md#需求-dot-tick-归属准确-场景-tick-同时缺失-sourceid-buffid
- [√] 1.2 在 `DalamudACT/DotEventCapture.cs` 移除/禁用 `TryResolveDotPairByDamage` 的来源推断路径，改为仅在状态唯一时推断来源，验证 why.md#需求-dot-tick-归属准确-场景-tick-同时缺失-sourceid-buffid

## 2. 文档更新
- [√] 2.1 更新 `helloagents/wiki/modules/battle.md`：对齐实际归因策略（不再按伤害强行推断来源），验证 why.md#需求-dot-tick-归属准确
- [√] 2.2 更新 `helloagents/CHANGELOG.md`：记录本次修复点

## 3. 安全检查
- [√] 3.1 执行安全检查（无敏感信息写入/无危险命令/不引入外部依赖）

## 4. 构建验证
- [√] 4.1 `dotnet build DalamudACT.sln -c Release`
