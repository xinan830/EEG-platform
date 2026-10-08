# Fix STFT narrow frequency ranges

## Why
An official static 8-13 Hz STFT Run reproduces `ANALYSIS_INPUT_INVALID`:
the service crops the matrix before integrating fixed 1-30 Hz bands.
Dynamic Runs can instead publish unavailable windows and an incorrect fallback
1-50 Hz axis. Valid requested subranges must not fail due to auxiliary bands.

## What Changes
- Integrate supplementary bands on the uncropped frequency grid of the same
  preprocessed signal, then crop only the returned matrix and axis.
- Preserve unavailable band values when Nyquist prevents full coverage.
- Build dynamic fallback axes from the requested range.
- Update STFT implementation identity to invalidate old erroneous cached results;
  keep spectrogram-v2 mathematics and historical artifacts unchanged.

## Impact
Backend service, STFT module and regression tests only. No WPF layout, channel,
filter, threshold, FFT, window scheduling or unit changes.
