# 显示滤波契约

实现位置：`backend/app/services/waveform_filter.py`。回放流式状态的兼容实现位于 `backend/app/eeg_core/stream_filter.py`。

## 当前参数

| 项目 | 当前值 |
|---|---|
| 高通/低切 | 默认 0.5 Hz |
| 低通/高切 | 默认 70 Hz |
| 带通原型 | Butterworth |
| 原型阶数 | 4 阶（SOS 表示） |
| 陷波 | 可关闭；开启频率 50 或 60 Hz |
| 陷波类型 | IIR notch，Q=30，转换为 SOS |
| 播放相位 | 因果 `sosfilt`，保留状态 |
| 基线稳定 | 可选，默认关闭 |

## 调用顺序

显示流的顺序是：

```text
去首样本 DC（可选基线策略）
  → 陷波（若开启）
  → Butterworth 带通
  → Montage
  → V 转 µV
```

静态窗口和播放必须使用相同参数；播放为了连续性保留 SOS 状态，修改参数时重建状态并按产品规则从头显示。

## 重要限制

`0.5–70 Hz` 只描述频率边界，不能唯一决定某个 sample 的数值。逐点复现还必须记录实现库版本、滤波相位、初始状态、边界策略、陷波 Q 值和调用顺序。

离线分析使用 `sosfiltfilt`，与显示的因果 `sosfilt` 不应期待逐点相同。
