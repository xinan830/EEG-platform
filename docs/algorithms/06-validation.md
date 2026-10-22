# 验证、复现与冻结证据

## 不同证据回答不同问题

| 证据 | 回答的问题 | 不能代替 |
| --- | --- | --- |
| 独立数值参考 | 独立公式/谱计算是否在固定容差内一致 | UI、性能或临床结论 |
| 冻结输入/输出回归 | 当前改动是否改变已登记场景 | 独立科学正确性 |
| 策略专项对照 | 全量、按需、组合及所声明复用是否一致 | 其他未验证方向 |
| 真实记录压力 | 特定记录/环境下耗时、内存、取消和复用表现 | 所有数据、设备或长期稳定性 |
| WPF 自动化与人工验收 | 请求、绑定、竞态、状态和追溯是否可用 | 科学公式验证 |

独立参考不导入生产计算函数。必须固定输入、参考身份、库版本及 rtol/atol，逐点/逐窗比较轴、数值、中间量、状态、范围、质量和 provenance，报告最大误差与失败坐标。轴、样本、单位和身份要求精确比较，不能用数值容差掩盖错位。

## 算法专属规则

不能把一个指标的 PSD floor、质量比例、频带边界或积分方法当成所有算法通则。RBP 当前为五频带；FAA 有 paired-epoch 门；Brainbeat 保留独立的积分与 epsilon；各模块的预处理/质量门以声明和测试为准。

冻结基线 JSON 与大数组 NPZ 分开保存，记录 checksum、环境、实现身份及 engineering-only 范围。新增基线不能覆盖旧件来掩盖差异；真实 EEG 和身份信息不纳入 Git。

## 入口

- [验证证据索引](../validation/README.md)。
- [官方算法管理与策略矩阵](official-algorithm-management.md)。
- [独立频谱参考端点](../architecture/independent-spectral-reference-validation.md)：固定 BDF/EDF 参考契约，容差不等于全部模块的容差。
- [当前结果边界回归](../validation/current-algorithm-result-contract-validation.md)。
