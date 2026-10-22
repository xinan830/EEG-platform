# 官方算法执行边界清单

更新日期：2026-10-22。本文只记录当前归属，整理过程与测试证据见[验收报告](../validation/algorithm-boundary-cleanup-acceptance.md)。策略支持和验证状态统一在[管理基线](official-algorithm-management.md)维护。

## 执行骨架

```text
WPF 参数/导联与策略选择
  -> Run 队列或按需窗口服务
  -> algorithm_runtime + builtin_modules 唯一工厂表
  -> 模块 resolve_inputs / execute_static / execute_dynamic / execute_window
  -> typed source -> recording context -> 读取/投影/预处理
  -> 模块独立计算 -> typed result
  -> 公共结果校验/序列化 -> Run + immutable artifact
  -> 当前格式校验 -> WPF handler / preview
```

公共层拥有身份、窗口规划、调度、取消、缓存/复用、结果校验及持久化。模块拥有公式、参数、通道角色、专属质量门和证据。组合策略不会增加第三份科学公式。

## 九算法所有权

包路径均相对 `backend/app/algorithms/`。每包提供 manifest、config 和 runner；必要时包含 compute、official、window 或 provenance。

| 算法包 | 科学输入 | 静态 / 动态结果 |
| --- | --- | --- |
| `psd/` | 所选分析通道 Welch spectrum | structured / structured-series |
| `stft/` | 所选分析通道 spectrogram | structured / structured-series |
| `rbp/` | 所选通道 Welch spectrum，模块固定预处理 | scalar 多频带输出 / series |
| `band_ratio/` | 单通道 Welch spectrum | scalar / series |
| `peak_frequency/` | 单通道 Welch spectrum | scalar / series |
| `iapf/` | 单通道 Welch spectrum | scalar / series |
| `theta_beta/` | 单通道 Welch spectrum，IAPF 相对频带 | scalar / series |
| `faa/` | F3/F4 配对时序，paired-epoch FFT | scalar / series |
| `brainbeat/` | Fz/Pz 配对频谱，逐窗 IAPF 与比值 | scalar / series |

## 输入边界

`backend/app/algorithm_runtime/signal_source.py` 定义 typed source 请求/数据；`backend/app/services/algorithm_recording_context.py` 负责校验角色顺序、采样率、绝对样本范围、单位、导联和处理身份，并适配到记录服务。

Welch、spectrogram、FAA 配对时序与 Brainbeat 配对频谱使用各自表示；连续预处理与缺样规则按模块声明执行。统一的是契约与校验，不是强行统一滤波、积分或质量阈值。

采集快照、基于记录通道的官方预设和兼容用户配置经后端验证后成为分析来源。原始通道投影产生 V/float64 时序；原始文件不改写。缓存身份绑定源、导联、滤波、通道顺序、范围及实现，不能混用不同配置的准备数据。

## 输出边界

`AlgorithmResult` / `AlgorithmSeriesResult` 承载标量或声明的多字段输出；RBP 主值可为空，五频带数值按声明提供。PSD/STFT 使用 `AlgorithmStructuredResult` / `AlgorithmStructuredSeriesResult`，大数组与轴元数据存 artifact。

`result_validation.py` 校验形状、单位、角色、并行序列及质量/失败；`run_result_serialization.py` 按结果类型序列化，不按算法 ID 实现科学规则。FAA 专属 provenance 由模块投影器解释。

完整官方结果为 `official-algorithm-result-v1`，标量动态格式为 `dynamic-analysis-frame-v6`。状态由模块显式提供，数值来自 canonical 输出；无重复顶层 value、warmup 回退或退休结果 reader。不合法当前结果返回结构化错误，不迁移已保存字节。

## 客户端与新增算法

WPF UI registry 和 Run result-handler registry 是两个客户端接入点。后端注册成功不能替代参数序列化、结果展示和桌面行为验收。

新增算法按[作者指南](official-algorithm-development.md)实施。新输入或输出形状可能需要扩展公共契约；不承诺只写一个文件即可完成接入。

数据库迁移、通用错误封装和 WPF 命名空间适配仍是独立边界，不能把官方 latest-only 表述为整个项目没有兼容代码。
