## 1. Playback state contract

- [x] 1.1 Notify `IsPlaying` and `IsCompleted` only when their values change.
- [x] 1.2 Keep position and TNK cursor publication on advancing ticks.
- [x] 1.3 Preserve full-frame restoration on actual playback start.

## 2. Regression verification

- [x] 2.1 Test that repeated playback ticks do not update waveform trace series.
- [x] 2.2 Test that state notifications occur on playback transitions, not between them.
- [ ] 2.3 Validate the same recording with an active Review algorithm panel in WPF.
- [ ] 2.4 Run OpenSpec validation, focused WPF tests, desktop build, and `git diff --check`.
