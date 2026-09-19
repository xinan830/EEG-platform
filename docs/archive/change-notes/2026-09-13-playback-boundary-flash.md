# 历史方案：扫屏边界闪烁补丁

这份边界清屏补丁已废弃。当前契约见 `2026-09-13-playback-continuous-context.md` 和 `docs/algorithms/10-playback-rendering.md`。

当前实现有固定页边界和覆盖式扫屏，但不在边界整屏清空。Worker 只增量绘制真实样本，内部缓冲轮换不得改变采样时间。
