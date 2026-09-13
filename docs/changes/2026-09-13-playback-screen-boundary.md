# 历史方案：按屏边界清屏

这份“切页即清空画布”的方案已废弃。当前契约见 `2026-09-13-playback-continuous-context.md` 和 `docs/algorithms/10-playback-rendering.md`。

当前播放仍按固定页边界切换，但不会清空画布；新页真实数据通过白色擦除带从左向右覆盖上一页视觉像素。
