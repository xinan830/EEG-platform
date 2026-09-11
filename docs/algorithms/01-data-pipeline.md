# 数据契约与处理流水线

## 输入

- 文件格式：BDF/EDF，由 MNE 读取。
- 内部矩阵：`numpy.ndarray`，形状 `(samples, channels)`，浮点，单位 V。
- `sfreq` 单位 Hz；文件时长为 `sample_count / sfreq`。
- 通道名与矩阵列严格对应；匹配别名：`T3→T7`、`T4→T8`、`T5→P7`、`T6→P8`。

## 显示窗口流水线

```text
BDF/EDF 原始数据 (V)
  → 读取所需通道和窗口
  → DisplaySignalFilter（因果 SOS）
  → Montage 线性组合
  → V × 1e6
  → API 返回 values_uv / elapsed_s
```

显示接口的 `start_s` 是文件绝对时间。调试台输入的秒数也直接解释为文件绝对时间，不叠加当前视窗起点。

## 离线分析流水线

```text
原始数据 (V)
  → preprocess_offline（整段零相位 SOS 带通）
  → 分 epoch、坏 epoch 质控
  → Welch PSD
  → IAPF / 频段功率 / FAA / 比值指标
```

离线指标当前默认使用原始记录参考，不自动套用显示窗口的 Montage；调用方必须显式提供通道映射。

## 单位与边界

- API 波形值统一为 µV，算法内部保持 V。
- 截止频率必须满足 `0 < low < high < sfreq/2`。
- 过短记录不能进行零相位滤波时必须返回结构化错误或质量失败，不能补零伪造。
- 滤波只影响派生结果，不覆盖原始文件。
