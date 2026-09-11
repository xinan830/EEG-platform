# 频谱与离线指标

实现位置：`backend/app/eeg_core/spectral.py`、`offline_metrics.py`、`faa.py`。

## 离线预处理

`preprocess_offline` 使用 4 阶 Butterworth 带通 `[1, 30] Hz`、SOS 表示、整段 `sosfiltfilt` 零相位处理。输入输出均为 V。

## Welch PSD

- epoch：4 s；50% 重叠配置用于步长计算。
- 每个 epoch 实际调用 Welch 时 `noverlap=0`，窗函数 Hann，`scaling="density"`，`detrend="constant"`。
- 峰值阈值：150 µV；clean epoch 比例至少 0.75，否则质量门失败。
- 输出频率限制为 1–30 Hz，PSD 不低于 `1e-20`。

## IAPF

在 3–30 Hz 拟合 `log10(PSD)` 对 `log10(frequency)` 的线性 1/f 背景，排除 7–13 Hz；在 7–13 Hz 计算残差功率。峰值相对背景突出度 ≥0.20 时取峰值，否则取残差功率重心（COG）。

## 频段与指标

- Delta `[1,4]`、Theta `[4,8]`、Alpha `[8,13]`、Beta `[13,30]`，使用闭区间梯形积分。
- RBP = 频段功率 / 四个频段功率总和。
- FAA = `ln(P_F4_alpha) - ln(P_F3_alpha)`，F3/F4 成对 epoch 质控。
- Brainbeat = Fz 相对 theta / Pz 相对 alpha。
- Fatigue = theta / beta，当前输出 Fz/Pz/Oz。
- HAI = `log10(beta[13,25] / low[1,8])`（实时纯函数）。

## 状态

这些指标是工程实现和可解释比值，不代表临床诊断结论；`analysis_contract.py` 中的版本和参数是唯一公开口径。
