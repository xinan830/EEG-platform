# 频谱与离线指标

## 算法版本

`offline-spectral-v3` 是冻结的数学基线。`offline-spectral-v4-configurable` 只开放分析范围、通道、动态分析范围长度和刷新步长，并复用 v3 的连续预处理、Welch、频段积分与质量门；它不会修改 PSD 数学结果。

v4 响应同时包含请求配置、实际执行配置、warm-up 状态和 12 位 `analysis_config_hash`。静态分析严格拒绝越过文件末尾；动态分析允许播放初期实际分析范围短于目标范围，但至少需要 4 秒数据。

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

FAA 独立使用 F3/F4：跳过记录前 12 s，2 s epoch、50% overlap、150 µV 成对质量门，至少 10 个 clean paired epochs，最长处理 1800 s。其功率单位在对数差中抵消。

## 状态

这些指标是工程实现和可解释比值，不代表临床诊断结论；`analysis_contract.py` 中的版本和参数是唯一公开口径。
