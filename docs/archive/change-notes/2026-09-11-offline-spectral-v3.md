# 离线频谱契约 v3

## 变更

锁定离线频谱的 4 秒 Hann segment、50% segment overlap、1–30 Hz 零相位 SOS Butterworth 预处理，以及边界插值后的频段积分。质量门定义为只平均 clean segment，clean 比例低于 75% 时整段不可用。

## 验证

新增 `backend/scripts/validate_spectrum_reference.py`，直接读取 BDF 并独立调用 SciPy 实现滤波、Welch 和积分，不调用生产频谱函数。新增合成正弦、segment 数量和边界积分测试。

## 影响

离线算法版本从 `offline-spectral-v2` 升级为 `offline-spectral-v3`。显示滤波和实时处理契约不变。尚未新增 Spectrum API 或前端页面。
