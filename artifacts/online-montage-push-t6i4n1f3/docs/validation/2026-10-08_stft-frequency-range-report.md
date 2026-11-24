# STFT 窄频段错误验证

8-13 Hz 是合法输入。本次修复后端在已裁剪矩阵上积分其他标准频段的错误，
没有改变滤波、FFT、4 秒 Hann 窗、1 秒 STFT 步长、质量阈值或输出单位。
科学版本保持 spectrogram-v2，实现身份更新为 stft-runtime-v2-band-axis，
不复用旧实现的动态不可用结果缓存，旧 artifact 不重算。

## 根因与修复路径

修复前以合成信号创建官方静态 8-13 Hz / 50 Hz 陷波 Run，重现
failed / ANALYSIS_INPUT_INVALID / 频段边界无效或超出频率轴。

SpectralAnalysisService.load_spectrogram 先裁剪到请求范围，再在该轴上积分
Delta 1-4、Theta 4-8、Alpha 8-13 和 Beta 13-30 Hz。部分边界不在轴上，
因此辅助统计使整个合法任务失败。

现在同一已滤波信号的 FFT 保留覆盖标准频段的内部网格，先计算辅助频段积分，
只在返回图表矩阵时裁剪。不能被奈奎斯特频率完整覆盖的频段返回 null，
不截短频段、不外推、不补零。动态失败分支也使用请求频段的频率轴，
不会使后续干净窗口因轴不匹配而失败。

对应代码：
- backend/app/services/spectral_analysis.py
- backend/app/algorithms/stft/runner.py
- backend/app/algorithms/stft/manifest.py

## 自动化证据

backend/tests/test_stft_frequency_ranges.py 新增 14 项测试，全部通过：
- 静态/动态 8-13、35-40、38-45 和 8.1-12.9 Hz 请求完成并保存 artifact。
- 单独读取合成原信号，SciPy 四阶 Butterworth、可选 50 Hz notch、periodogram
  复算矩阵，容差 rtol=1e-11、atol=1e-24 V²/Hz。
- 标准频段梯形积分独立复算，容差 rtol=1e-11、atol=1e-12 µV²。
- 40 Hz 采样率下合法 8-13 Hz 请求不因 Beta 不可用而失败。
- 动态首窗质量拒绝或 ValueError 后，后续干净窗口仍有结果，轴一致。

测试信号包含 10 Hz 和 39 Hz 成分，避免高频范围测试因纯 Alpha 信号被
滤掉后触发 flatline；这是测试输入修正，不是放宽质量门。

全量后端：373 passed。测试客户端存在两个弃用警告。

复现命令，在 backend 目录执行：

```powershell
uv run --frozen --extra dev pytest -q tests/test_stft_frequency_ranges.py
uv run --frozen --extra dev pytest -q
```

## 本地真实记录证据

用临时数据库和 artifact 目录注册本地已完成记录，不修改原始 EEG，
不创建运行中应用数据库的新 Run。源 SHA-256：
1c3a9ef23771d6b768ec31bd9f072d9504d32d4149fe799dbd8e976f27465983。
时长 149.982 秒、采样率 1000 Hz，选择 O1，陷波 50 Hz。
动态外层窗口 5 秒，步长 1 秒。

| 请求频段 Hz | 静态结果矩阵 | 动态结果矩阵 | 结果 |
| --- | --- | --- | --- |
| 8-13 | 146 x 21 | 146 x 2 x 21 | 两次 completed，返回轴 8-13 |
| 35-40 | 146 x 21 | 146 x 2 x 21 | 两次 completed，返回轴 35-40 |
| 38-45 | 146 x 29 | 146 x 2 x 29 | 两次 completed，返回轴 38-45 |

6 份 artifact 校验和读取成功；静态矩阵均有完整有限值。
动态均为 145 个 Complete、1 个 Partial；4 秒 warm-up 窗只有一条内部
时频行，另一行按既有契约留 NaN，不是拒绝或丢失数据。

## 验收边界

真实后端执行通过不等于 WPF 实际显示已验收。本轮未修改 WPF。
请重启后端，选 O1 / 8-13 Hz / 50 Hz 陷波，重新分析，确认图表和溯源。
WPF 旧进程还需重新编译启动才能显示此前更新的执行按钮样式。
这是工程验证，不代表临床有效性。当前未提交、未推送。
