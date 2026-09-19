# 频谱与离线指标

## 算法版本

`offline-spectral-v3` 是冻结的数学基线。`offline-spectral-v4-configurable` 只开放分析范围、通道、动态分析范围长度和刷新步长，并复用 v3 的连续预处理、Welch、频段积分与质量门；它不会修改 PSD 数学结果。

v4 响应同时包含请求配置、实际执行配置、warm-up 状态和 12 位 `analysis_config_hash`。静态分析严格拒绝越过文件末尾；动态分析允许播放初期实际分析范围短于目标范围，但至少需要 4 秒数据。

动态指标共用同一时间语义：少于 4 秒时不产生 PSD 指标；4 秒至所选分析窗口前，从记录起点累积计算并标记为预热；达到所选窗口后使用固定长度的滑动分析范围。IAPF 与 Theta/Beta 使用相同的动态窗口规则；IAPF 只在 PSD 的 1/f 校正与 Peak/COG 选择上拥有自身的数学逻辑。

实现位置：`backend/app/eeg_core/spectral.py`、`offline_metrics.py`、`faa.py`。

## 离线预处理

`preprocess_offline` 使用 4 阶 Butterworth 带通 `[1, 30] Hz`、SOS 表示、整段 `sosfiltfilt` 零相位处理。输入输出均为 V。

## Welch PSD

- 外层分析范围被切成 4 s 频谱计算片段，步长 2 s，即计算片段间 50% overlap。例如 30 s 分析范围有 `(30-4)/2+1=14` 个候选计算片段。
- 质量门逐个检查计算片段；只平均 clean 计算片段，clean 比例低于 0.75 时整段不可用。
- 每个 clean 计算片段调用一次 Welch：`nperseg=4 s`、`noverlap=0`、Hann、`scaling="density"`、`detrend="constant"`。这里的 50% overlap 是外层 4 s 计算片段之间的重叠，不是单个 4 s 计算片段内再次重叠。
- 峰值阈值：150 µV；clean epoch 比例至少 0.75，否则质量门失败。
- 输出频率限制为 1–30 Hz，PSD 不低于 `1e-20`。

多通道 PSD 逐通道计算后按通道请求顺序返回。后端内部 PSD 单位为 `V^2/Hz`，API 边界乘 `10^12` 转为 `uV^2/Hz`。

## IAPF

在 3–30 Hz 拟合 `log10(PSD)` 对 `log10(frequency)` 的线性 1/f 背景，排除 7–13 Hz；在 7–13 Hz 计算残差功率。峰值相对背景突出度 ≥0.20 时取峰值，否则取残差功率重心（COG）。

## 频段与指标

- Delta `[1,4)`、Theta `[4,8)`、Alpha `[8,13)`、Beta `[13,30]`；实现用边界线性插值后梯形积分，避免端点重复或丢失面积。
- RBP = 频段功率 / 四个频段功率总和。
- FAA = `ln(P_F4_alpha) - ln(P_F3_alpha)`，F3/F4 成对 epoch 质控。
- Brainbeat = Fz 相对 theta / Pz 相对 alpha。
- Fatigue = theta / beta，当前输出 Fz/Pz/Oz。
- HAI = `log10(beta[13,25] / low[1,8])`（实时纯函数）。

频段积分会先用线性插值补入精确边界，再用梯形积分。离散频点未必落在 1、4、8、13、30 Hz，因此测试应使用相对误差，不应要求浮点严格相等。v3 的 RBP 分母是四个标准频段功率之和；在该分段下等价覆盖 1–30 Hz。

FAA 有两条明确隔离的时间契约，不能混用：

- **官方静态 FAA Run**：严格使用用户请求的绝对分析范围，不隐式丢弃该范围开头的数据；在这段范围内使用 2 s epoch、50% overlap、150 µV 成对质量门，至少需要 10 个 clean paired epochs。
- **旧实时/报告流程**：仅在整段记录的历史流程中丢弃记录起始 12 s，最长处理 1800 s；这不是静态 Run 的规则。

两条流程的 FAA 公式均为 `ln(P_F4_alpha) - ln(P_F3_alpha)`，功率单位在对数差中抵消。

## 官方算法运行状态

- **相对频段功率（RBP）**：可静态运行。一次结果返回 Delta、Theta、Alpha、Beta 四项相对功率；它们共用同一通道、PSD、质量门和 1–30 Hz 分母，不是四次独立计算。
- **额叶 Alpha 不对称性（FAA）**：可静态运行。用户明确选择 F3 与 F4 的原始来源通道；后端按请求的绝对范围和成对 2 s epoch 质量门计算 `ln(P_F4)-ln(P_F3)`。不足十个 clean 成对 epoch 时结果为 `null`，不会显示为零。
- **脑节律指数（BrainBeat）**：仍为工程验证中。它的正式实时语义包含 IAPF 锁定和 EMA 状态；当前离线 Run 没有可持久化的实时会话状态，因此不能把一次静态公式冒充为同一算法结果。

## 状态

这些指标是工程实现和可解释比值，不代表临床诊断结论；`ANALYSIS_CONTRACT` 是离线频谱口径，`LIVE_ANALYSIS_CONTRACT` 单独记录旧实时链路的状态性参数。
