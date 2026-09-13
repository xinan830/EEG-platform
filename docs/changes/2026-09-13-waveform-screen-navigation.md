# Waveform screen navigation and timeline seeking

> 历史变更记录：后续播放视觉已改为固定分页覆盖式扫屏，波形区红色播放头已移除。当前契约以 `docs/algorithms/10-playback-rendering.md` 为准。

- Added previous-screen and next-screen controls. One action moves by the configured screen duration and clamps at the recording boundaries.
- Manual screen navigation stops playback, loads the target screen and positions playback at that screen's left edge.
- Pause/resume continues from the paused position; it does not rewind to the screen start.
- The recording overview supports click-to-position and absolute pointer-based window dragging.
- A live tooltip shows the pointer time and the target screen range before the request is committed.
- The original red waveform playhead was later removed; the white erase gap now marks the write frontier.
- Event markers on the overview now use whole-recording coordinates instead of current-screen coordinates.
