# TNK 策略 parity 证据

运行入口：`backend/tests/test_tnk_strategy_parity.py`

本次证据使用冻结的解析 PSD 窗口、固定采样率、样本边界和配置。每个可声明的 TNK 模块都分别执行了 full 序列、从同一 reset 起点重放到目标窗口的 on-demand 序列，以及同一目标结果的 combined 对照；结果签名包含值、结构化值、质量、失败、请求/实际范围和 evidence。每个结果还保存了确定性 checksum。机器可重放摘要见 [tnk-strategy-parity.json](tnk-strategy-parity.json)。

结果：12 个 TNK 身份均被覆盖。11 个窗口/组合路径的目标结果与重放结果一致；FAA 按定义仅支持完整 paired segment，任意游标 on-demand 显式为 `TNK_FAA_SEGMENT_SCOPE_REQUIRED`，完整 segment 的 full/combined 一致。

冻结参考门槛已通过：本次使用 `python 3.10.20`、`numpy 2.2.6`、`scipy 1.15.3`、`specparam 2.0.0rc6`。报告同时记录输入频率轴和 PSD 的形状、单位与 checksum，以及每个算法目标结果的数值、状态、质量、范围、provenance、证据和 checksum。
