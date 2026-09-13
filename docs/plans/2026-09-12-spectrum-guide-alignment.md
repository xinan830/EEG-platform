# EEG 频谱指南对齐实施计划

## 目标

将指南中正确的架构建议落到现有项目，同时保持 `offline-spectral-v3` 的数学结果不变。前端只请求和渲染后端结果，不重新执行 FFT、Welch、滤波、积分或 dB 数学计算。

## 版本层级（必须保持分离）

- `analysis_algorithm_version = offline-spectral-v3`：滤波、Welch、PSD、Band Power、RBP 的数学契约。
- `spectrogram_contract_version = spectrogram-v2`：Spectrogram 的时间坐标、矩阵结构、单位和质量语义。
- `offline-spectral-v3` 不因 Spectrogram 接口升级而改变；API 同时回显两个版本字段。

## 实施顺序

### P0-1：统一 Active Analysis Range

- 在应用层建立唯一的 active range 状态：`start_s`、`end_s`、来源。
- 来源枚举固定为：`custom`、`current_30s`、`current_screen`、`selection`、`dynamic`。
- PSD、RBP、Spectrogram、算法校验均使用同一范围。
- 草稿输入不得立即改变 active range，只有提交后生效。
- 手工输入：只修改 draft，点击“分析该区间”后 commit。
- “当前 30 秒”和“当前屏幕”：计算后直接 commit。
- 波形框选：写入 draft，等待用户点击“分析该区间”后 commit；不得一边填表一边偷偷请求。
- 后端必须回显 `requested_range` 和 `actual_range`。

验收：同一提交区间下，PSD 和 Spectrogram 请求的起止时间完全一致；切换波形屏幕不会覆盖用户已提交的自定义区间。

### P0-2：修复 Spectrogram 时间语义

- `times_s` 明确定义为每个 4 秒窗口的中心时间。
- 保留窗口起止信息可推导性：`center ± segment_s/2`。
- 将 Spectrogram 契约升级为 `spectrogram-v2`，不静默改变旧版本语义；这不是 PSD 算法升级。
- 增加时间 bin、频率 bin、矩阵形状元数据。
- 质量不合格的时间窗保留时间位置，功率使用 `null/NaN`，并回显质量原因。

验收：10–40 秒区间返回第一个中心 12 秒、最后一个中心 38 秒、27×117 矩阵。

### P0-3：分离线性功率和 dB 显示值

- 后端保留线性 `uV²/Hz`，字段语义为 `power_linear`。
- 后端同时提供 `dB re 1 µV²/Hz` 显示字段。
- dB 定义固定为 `10 * log10(P / (1 µV²/Hz))`；当 P 的数值单位为 µV²/Hz 时，数值计算为 `10 * log10(P)`。
- 前端禁止自行执行 `10*log10`。

验收：算法校验可同时看到两种单位，前端渲染只读取后端 dB 字段。

### P0-4：数学 golden regression

- P0 改造前后的 `offline-spectral-v3` PSD、Band Power、RBP 数值必须保持不变。
- 使用已验证 BDF 的固定区间和通道作为 golden fixture。
- golden 测试与 Spectrogram contract 测试分离：前者验证数值，后者验证中心时间、矩阵形状、单位和质量语义。

### P1：补全 Spectrogram 校验与质量信息

- 返回 `time_bins`、`frequency_bins`、`matrix_shape`、`first_center_s`、`last_center_s`。
- 明确坏窗保留时间位置，不删除时间轴。
- 算法校验弹窗展示这些字段。

### P1：图表库统一

- 先迁移 RBP 到 ECharts。
- 再迁移 PSD 到 ECharts。
- 最后迁移 Spectrogram 到 Heatmap。
- 迁移过程中不得改变后端数据和时间语义。

### P2：动态时频图和性能

- 动态 Spectrogram 使用最近窗口和固定刷新步长。
- 增加长文件分段加载、请求取消和内存基准。

## 本轮执行范围

本轮执行 P0-1、P0-2、P0-3、P0-4 的后端契约和前端消费改造，并补充回归测试。ECharts 迁移在契约测试和 golden regression 通过后进行。

## 禁止事项

- 不修改 `offline-spectral-v3` 的滤波、Welch、频段和 RBP 定义。
- 不在前端重新计算 EEG 数学结果。
- 不把 Viewer 的 0.5–70 Hz 显示滤波与 Analysis 的 1–30 Hz 契约混合。
