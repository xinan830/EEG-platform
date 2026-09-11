# 滤波契约与静态/播放一致性

## 变更

- 固定阅图滤波契约：因果（causal）处理、逐采样点 EMA 去直流（alpha=0.01）、IIR 陷波 Q=30、4 阶 Butterworth 带通原型（SciPy 变换后的数字实现总阶数为 8）。
- 播放和静态窗口统一使用 50 ms 处理块；滤波器状态按采样持续，不再按块均值更新 DC 状态。
- 静态窗口从文件起点推进到目标位置后截取；播放拖拽定位时先从文件起点预热到目标位置，消除定位后的初始状态差异。
- API 返回 `filter_contract`，便于算法回归、问题复现和第三方核对。

## 事实与边界

- “0.5–70 Hz”仅是截止频率，当前实现还明确了滤波类型、阶数、相位、陷波 Q、去直流方式和块大小。
- 这是显示链路契约，不覆盖 `backend/app/eeg_core/processor.py` 中的离线指标处理；两条链路必须分别记录各自参数。
- 当前首次访问远处窗口仍需从文件起点建立滤波状态；命中检查点后只从最近检查点继续。检查点缓存的跨进程共享和压力验收属于后续 P2 工作。

## 验证

- `test_display_filter_is_invariant_to_chunk_boundaries` 验证一次处理与任意分块处理逐点一致。
- 后端测试覆盖 Montage 完整通道校验、静态数据流和独立滤波黄金值，当前 34 个测试通过。
- 证据路径：`backend/app/services/waveform_playback.py`、`backend/app/services/recordings.py`、`backend/tests/test_waveform_playback.py`。
