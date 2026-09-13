# Spectrum plan revision

- Separated `offline-spectral-v3` (mathematical analysis algorithm) from `spectrogram-v2` (time/matrix/unit contract).
- Defined dB values as `dB re 1 uV²/Hz` and retained linear power for validation and future analysis.
- Fixed range-source vocabulary and documented commit semantics for manual ranges, shortcuts and selection.
- Added the P0 golden regression requirement: PSD, Band Power and RBP values must not change during contract/UI migration.
- Changed waveform selection to draft-then-commit: selection fills the static range inputs and analysis starts only after clicking “分析该区间”.
- Added per-window Spectrogram quality metadata using the shared 150 µV peak threshold; bad windows retain their time positions and render as NaN/gray gaps.
- Added API regression coverage for the split `offline-spectral-v3` / `spectrogram-v2` versions, linear+dB payloads, matrix shape and quality envelope.
- Added bad-window details to the Spectrogram validation dialog.
