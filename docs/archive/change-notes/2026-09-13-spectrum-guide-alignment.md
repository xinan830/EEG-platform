# Spectrum guide alignment (P0)

- Added a shared application-level active analysis range and connected SpectrumPanel and SpectrogramPanel to it.
- Static PSD and static Spectrogram now submit the same active start/end range.
- Spectrogram time bins now represent window centers (for example, 10–14 s is reported as 12 s).
- Spectrogram responses now include both linear `uV²/Hz` power and backend-computed `dB(uV²/Hz)` values.
- Added matrix shape, time/frequency bin counts, first/last center metadata and a read-only Spectrogram algorithm validation dialog.
- Spectrogram rendering consumes backend dB values and no longer performs logarithmic conversion in Vue.
- Dynamic Spectrogram requests use the playback position as the right boundary and do not request future samples during warm-up.

## Verification

- Backend: 93 tests passed.
- Frontend Vitest: passed.
- `vue-tsc --noEmit`: passed.

ECharts migration was intentionally not forced in this change because package installation did not complete in the current environment. The existing rendering remains functional; chart-library migration is a separate UI-only task after the data contract is stable.
