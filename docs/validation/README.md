# 验证证据索引

更新日期：2026-10-22。本目录保存工程验证与受控回归夹具，不是当前功能清单。当前行为入口见[算法管理](../algorithms/official-algorithm-management.md)与[项目导航](../README.md)。

## 当前验收入口

- [算法执行边界整理与官方导联预设修复](algorithm-boundary-cleanup-acceptance.md)：已验收提交 `861ba46`，完整后端 1,049 项、WPF 917 项通过。这是该提交的证据，不保证后续改动自动通过。
- [当前官方结果契约](current-algorithm-result-contract-validation.md)：typed 状态、当前格式、窗口与消费者边界。
- [结果边界冻结回归](algorithm-result-boundary-validation.md)：36 场景、58 数组、172,448 点及摘要/provenance 对照。
- [九算法组合策略](2026-10-21-official-combined-strategies.md)：合成输入的数值、状态、范围、provenance 与取消隔离。
- [九算法真实记录服务测量](2026-10-21-official-algorithm-real-recording-pressure.json)：该记录和环境的测量数据，不代表全部配置、长时间或硬件验收。
- [PSD 真实记录组合调度压力](2026-10-17-background-full-orchestration-real-psd.md)：不可推广成九算法所有组合路径的压力证据。
- [真实记录回归](real-recording-regression.md)：固定授权记录的 REF/AVG、九算法静态及动态重放、独立参考与不可覆盖基线。

## 受控冻结件

| 摘要/矩阵 | 大数组 | 用途 |
| --- | --- | --- |
| [独立数值基线](official-algorithm-numeric-baseline.json) | [NPZ](official-algorithm-numeric-baseline-arrays.npz) | 独立谱/指标参考身份、环境和容差 |
| [策略输出基线](official-algorithm-strategy-baseline.json) | [NPZ](official-algorithm-strategy-baseline.npz) | 九算法动态系列输出冻结 |
| [策略与契约矩阵](official-algorithm-strategy-matrix.json) | 无 | manifest/schema 指纹、声明及验证入口 |
| [结果边界基线](algorithm-result-boundary-baseline.json) | [NPZ](algorithm-result-boundary-baseline.npz) | 结构整理前后完整结果对照 |

JSON 中记录 checksum、环境与参考身份；实际容差必须读对应基线，不能将 API 参考端点的容差与测试基线混用。本轮文档整理不改写任何 JSON/NPZ，13 件文件的 SHA-256 前后核对见[整理记录](../../openspec/changes/refresh-current-project-documentation/validation-report.md)。

## 带日期的历史证据

其余日期命名报告，以及 `algorithm-audit-plan-2026-10-20.md`、`algorithm-runtime-reset-progress-2026-09-16.md` 等阶段记录，均按原时间和提交语境阅读。它们可能记录当时未完成事项、后来退休的 Vue 页面、shadow CLI、图/公式执行器或旧接口；这些内容不代表当前仍提供相应功能，也不构成当前待办。

保留这些报告是为了复查当时的测量、失败和修复依据，不维持退休实现。当前进度以对应 OpenSpec tasks 为准，不在历史报告重写测试数字或把当时的失败改成成功。旧说明中的实现命令需先按当前代码确认，不能直接执行。

原始 EEG、真实身份、数据库、机器日志和运行缓存不进入本目录。真实记录验证必须已有授权，派生证据也要控制身份暴露。
