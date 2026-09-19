# 显示滤波器 SOS v2

## 算法契约

- 算法版本：`display-iir-sos-v2`。
- 带通：4 阶原型 Butterworth，`output="sos"`，因果 `sosfilt`。
- 陷波：`iirnotch`、Q=30，转换为 SOS 后使用因果 `sosfilt`。
- 状态初始化：`sosfilt_zi` 乘首样点；状态跨 50 ms 数据块连续保存。
- 基线稳定：逐样点 EMA，alpha=0.01，默认关闭且由 API/UI 显式控制。
- 顺序：可选 EMA、可选 notch、带通、Montage、V 转 µV。

## 一致性

- 静态窗口、连续播放、调试采样和算法检验共享 `DisplaySignalFilter`。
- 检查点缓存键包含基线稳定状态，不会错误复用另一种处理状态。
- 独立 SciPy 参考实现与生产滤波器逐点比较；另验证分块、预热和检查点恢复。
- `scripts/validate_display_filter_independent.py` 不导入生产算法，可直接从 BDF/EDF 独立复算指定绝对时间点。

## 行为变化

- 默认不再执行隐藏 EMA，因此同一 BDF 的历史逐点 µV 数值会变化。
- 从直接传递函数 `b/a + lfilter` 改为 SOS，提高低截止高阶带通的数值稳定性。
- UI 明确显示“基线稳定：开启/关闭”，实际处理不再与状态栏描述不一致。

## 边界

- 这是阅图显示滤波契约，不替代 PSD、IAPF、RBP 或 ERP 的独立分析预处理契约。
- 疑似坏道仍由用户明确排除；本次没有加入静默自动坏道判断。
