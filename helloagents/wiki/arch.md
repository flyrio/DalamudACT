# 架构设计

## 总体架构
```mermaid
flowchart TD
    A[游戏事件 Hook] --> B[事件解析/过滤]
    B --> C[战斗数据聚合]
    C --> D[统计计算]
    D --> E[UI 展示]
```

## 技术栈
- **后端:** C# / .NET / Dalamud
- **前端:** ImGui（Dalamud UI）
- **数据:** 游戏内事件与 Lumina 数据表

## 核心流程
```mermaid
sequenceDiagram
    Client->>Plugin: Ability/ActorControl 事件
    Plugin->>Battle: 记录伤害/DoT/状态
    Battle->>UI: 汇总统计
    UI-->>Client: 名片/列表显示
```

## 重大架构决策
完整的ADR存储在各变更的how.md中，本章节提供索引。

| adr_id | title | date | status | affected_modules | details |
|--------|-------|------|--------|------------------|---------|
