# 实时处理器模块拆分

## 背景

`backend/app/eeg_core/processor.py` 同时承担初始化、流式滤波、会话缓冲、IAPF
锁定、实时评分、BrainBeat、疲劳和 RBP 计算，超过 1,400 行。该结构违反了变更发生
时适用的 400 行硬上限，也使滤波状态或指标公式的局部修改难以独立审查。

## 变更

- `processor.py` 仅保留 `EEGProcessor` 公共入口及状态初始化。
- `stream_filter.py` 管理 SOS 系数、跨块状态、原始环缓冲及参数变更后的重建。
- `processor_state.py` 管理全局 reset、通道选择和会话计时状态。
- `processor_sessions.py` 管理校准、报告时段和 FAA 生命周期。
- `processor_iapf.py` 管理 IAPF 锁定以及 1/f、HAI 派生状态。
- `processor_metrics.py` 计算单帧综合指标。
- `processor_brainbeat.py` 计算 BrainBeat、疲劳和相对频段功率。
- `realtime_spectral.py` 提供无状态的实时频谱纯函数。
- `iapf_diagnostics.py` 负责 IAPF 日志文本格式。
- 保留 `EEGProcessor` 的导入路径和所有方法名；本次不改变阈值、公式、滤波顺序、
  warmup、settle 或返回结构。
- 删除 `processor.py` 的旧尺寸豁免；后续文件规模门禁已改为分级评审策略，参见
  `2026-09-11-code-size-soft-limit.md`。

## 回归保护

- 纯函数特征测试固定频段积分、相对功率、HAI、1/f 残差、BrainBeat 映射和日志文本。
- 流式滤波测试覆盖整块与分块一致、reset 与新实例一致、参数重建与新实例一致，
  以及窗口加 settle 的出帧时序。

## 风险与边界

这是结构重构，不是算法验证。测试能证明受覆盖输入的行为保持一致，但不能把遗留实时
算法结果提升为临床真值。临床用途仍需固定数据集、独立实现对照和版本化验收阈值。
