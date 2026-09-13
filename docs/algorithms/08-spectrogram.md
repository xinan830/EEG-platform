# 时频图算法与数据契约

## 版本边界

- 数学预处理基线：`offline-spectral-v3`。
- 时频数据契约：`spectrogram-v2`。
- 可配置接口标识：`spectrogram-v2-configurable`。

`spectrogram-v2` 修改的是时间坐标、矩阵、单位和质量状态的表达，不代表 v3 的滤波、PSD 或频段积分发生变化。

## 计算步骤

```text
整段原始 EEG (V)
  → offline-spectral-v3 整段 1–30 Hz 零相位预处理
  → 截取请求的实际时间范围
  → 4 s 窗口，以 1 s 步长滑动
  → 每窗去均值、Hann 加窗、rFFT
  → 单边 PSD density 归一化
  → 保留 1–30 Hz
  → V^2/Hz 转 uV^2/Hz
```

长度为 `T` 秒时，完整窗口数是 `floor((T-4)/1)+1`。例如 10–40 s 共 30 s，窗口为 10–14、11–15，直到 36–40，共 27 个时间 bin。

## 时间轴和矩阵

时间坐标是窗口中心，不是窗口起点。10–14 s 的中心为 12 s。响应矩阵按 `time × frequency` 组织，`matrix_shape = [time_bins, frequency_bins]`，每个通道各有一份矩阵。

质量失败的窗口不会被删除：时间中心保留，对应功率使用 NaN/非有限值，`quality.windows` 给出 `bad` 及原因。这样热力图不会因为坏窗而把后续时间向左移动。当前坏窗原因包括非有限样本和去均值后峰值超过 150 µV。

## 两种功率表示

- `power_linear`：`uV^2/Hz`，用于积分、验证和定量读取。
- `power_db`：`dB re 1 uV^2/Hz`，只用于更易读的显示。

若线性数值已经以 `uV^2/Hz` 表示，则：

```text
P_db = 10 × log10(P_linear)
```

其完整含义是 `10×log10(P / (1 uV^2/Hz))`。频段趋势由后端对每个时间窗的线性 PSD 做频率积分，单位 `uV^2`；前端不从热力图重新计算趋势。

## 显示与数学分离

前端可只展示 Delta、Theta、Alpha、Beta 或自定义 1–30 Hz 子区间，但后端保留完整频率矩阵。轴刻度抽稀、色标、hover、高亮和 ECharts 渲染不改变算法结果。
