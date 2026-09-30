# Design

The official STFT Run carries `low_hz`, `high_hz`, and `notch_hz` through
`OfficialAlgorithmRunConfig`, the runtime `StftConfig`, the recording context,
and `SpectralAnalysisService.load_spectrogram`.

`low_hz/high_hz` are the shared analysis/filter range for this iteration. The
returned frequency axis is clipped to that range. The preprocessing cache key
includes the selected range and notch so incompatible filtered signals cannot
be reused.

The scientific primitive receives the requested output range explicitly. It
rejects a high edge at or above Nyquist rather than silently clipping it.
This keeps a 50 Hz default valid only for recordings sampled above 100 Hz.
