# DalamudACT

> 本文件包含项目级别的核心信息。详细的模块文档见 `modules/` 目录。

---

## 1. 项目概述

### 目标与背景
提供基于 Dalamud 的战斗伤害统计与展示插件。

### 范围
- **范围内:** 伤害事件采集、统计汇总、UI 展示
- **范围外:** 服务器端解析、跨客户端同步、外部数据存储

### 干系人
- **负责人:** 维护者/插件作者

---

## 2. 模块索引

| 模块名称 | 职责 | 状态 | 文档 |
|---------|------|------|------|
| core | Hook 与事件采集 | ✅稳定 | [core](modules/core.md) |
| battle | 战斗数据聚合与统计 | ✅稳定 | [battle](modules/battle.md) |
| ui | 可视化与交互 | ✅稳定 | [ui](modules/ui.md) |
| potency | 技能与 DoT Potency 数据 | ✅稳定 | [potency](modules/potency.md) |

---

## 3. 快速链接
- [技术约定](../project.md)
- [架构设计](arch.md)
- [API 手册](api.md)
- [数据模型](data.md)
- [变更历史](../history/index.md)
