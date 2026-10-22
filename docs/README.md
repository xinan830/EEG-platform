# 项目文档导航

更新日期：2026-10-22。本文索引当前人工说明，公共行为与科学契约以 `openspec/specs/` 为准；发现规范与代码冲突，应记录差异并另行处理，不能通过改说明掩盖。

## 从这里开始

- [项目入口](../README.md)：启动和技术栈。
- [交接说明](../HANDOFE.md)：已验收基线和维护边界。
- [工程开发契约](工程开发契约.md)：数据、职责、文件规模、质量与提交要求。
- [当前能力与后续方向](roadmaps/eeg-research-platform-master-plan.md)：已实现能力及尚未承诺的方向。

## 算法与数据链路

- [算法文档索引](algorithms/README.md)。
- [新增算法与维护指南](algorithms/official-algorithm-development.md)。
- [管理基线与策略矩阵](algorithms/official-algorithm-management.md)。
- [执行边界清单](algorithms/official-algorithm-execution-inventory.md)。
- [统一追溯架构](architecture/traceable-analysis-foundation.md)。
- [科学实现归属](architecture/scientific-backend-ownership.md)。

## 桌面、服务与扩展

- [采集与阅图](architecture/desktop-acquisition-review.md)。
- [WPF 模块边界](architecture/desktop-client-module-boundaries.md)。
- [项目与批量 Run](architecture/projects-and-batch-runs.md)。
- [结果、验证与导出](architecture/results-validation-and-export.md)。
- [Definition 元数据校验](architecture/algorithm-definition-engine.md)。
- [闭集原语与校验词汇](architecture/research-primitives.md)。
- [扩展 manifest 边界](architecture/extension-governance.md)。
- [独立频谱参考验证](architecture/independent-spectral-reference-validation.md)。

## 证据与历史

[验证索引](validation/README.md)区分当前验收入口、冻结夹具和带日期的历史证据。历史报告只证明其当时范围，不代表退休功能仍存在。

OpenSpec 的变更目录保留计划、任务和验收决策；完成且归档的记录在 `openspec/changes/archive/`。被替代的临时笔记、研究稿与实施草案已删除，需要时从 Git 历史查找。不要在 `docs/` 新建第二套变更日志或任务体系。

主规范中历史 shadow 迁移与浏览器算法工作台已加状态提示，并指向后续退役规范；其原条款保留用于查阅决策，不能当作当前执行入口。规范后续整合需要单独核对 requirements，而非按旧页面恢复功能。
