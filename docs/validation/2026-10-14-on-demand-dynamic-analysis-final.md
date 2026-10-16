# 按需动态分析最终收口报告

日期：2026-10-14
分支：`feature/on-demand-dynamic-analysis`

后续状态：本文件保留 2026-10-14 自动化收口时的范围与数字。2026-10-15 用户已逐项反馈九个算法的全量/按需正常路径可用；新增人工验收和剩余综合项见 [WPF 人工验收记录](2026-10-15-offline-modes-wpf-acceptance.md)。下文“未宣称事项”是当时状态，当前进度以该补充记录及 tasks 为准。

## 结论

按需动态分析的后端基础设施、PSD、RBP、四个标量算法、FAA、Brainbeat 和 STFT 迁移均已完成自动化验证；用户已逐阶段确认 GUI，最近一次确认覆盖 STFT 阶段。生产开关仍保持关闭，未在本次收口中擅自改变运行模式。

这份报告证明自动化、构建和契约检查通过，不替代用户对最终整体验收的确认。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Backend 全量测试 | 657 passed |
| WPF 全量测试 | 652 passed |
| Release build | 0 errors；保留 6 个既有 NU1701 警告 |
| OpenSpec strict | `Change 'on-demand-dynamic-analysis' is valid` |
| 文件规模检查 | 通过；4 个已登记的历史超 400 行文件按策略豁免 |
| `git diff --check` | 通过 |

## 功能范围

- 动态窗口具有持久化 session/window/attempt/subscription 身份和生命周期。
- 结果发布具备 schema、单位、shape、provenance、checksum 和原子发布校验。
- worker 具有有界资源、幂等去重、取消、重试和进程恢复语义。
- PSD、RBP、BandRatio、PeakFrequency、IAPF、ThetaBeta、FAA、Brainbeat 和 STFT 已完成按需能力适配及对应测试。
- WPF 详情页和 Review 通过共享 coordinator/provider 消费后端窗口结果；前端不重算科学结果。
- 回溯自动播放按算法声明的 `refresh_step_s` 合并游标 tick；同一动态窗口不会因播放计时器连续更新而被重复取消，新增回归测试已覆盖。
- 旧完整 Run、旧 artifact 和生产开关关闭时的旧路径保持可用。

## 开关状态

后端 `BRAIN_ENABLE_ON_DEMAND_*` 和 WPF `BRAIN_WPF_ON_DEMAND_*` 的默认值仍为 `0`。自动化测试通过显式 opt-in 验证按需路径；生产启用仍应在最终整体验收后单独决定。

## 未宣称事项

- P3 中“真实服务不可用时波形仍独立播放”的完整 GUI 验收仍未单独勾选。
- 阶段 6 的用户最终整体验收仍未勾选。
- 当前工作树包含本项目的大量未提交改动及既有 `.verify-*` 目录；本次没有提交、推送或清理这些内容。

## 可追溯入口

- 分阶段计划：`openspec/changes/on-demand-dynamic-analysis/tasks.md`
- 能力矩阵：`docs/algorithms/on-demand-dynamic-capability-matrix.md`
- 阶段报告：`docs/validation/2026-10-13-on-demand-psd-validation-report.md`、`docs/validation/2026-10-13-on-demand-faa-stage3.md`、`docs/validation/2026-10-14-on-demand-brainbeat-stage4.md`、`docs/validation/2026-10-14-on-demand-stft-stage5.md`
