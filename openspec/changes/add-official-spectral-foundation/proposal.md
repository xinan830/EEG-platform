# Add official spectral foundation

## Why

The platform already has frozen Welch PSD and spectrogram services, but the
Fourier transform is still an implementation detail inside those calculations.
The next official algorithm phase needs one explicit scientific foundation for
FFT frequency axes, units, sample coordinates, and transform padding evidence.

## Scope

- Add a pure, SI-unit Fourier transform primitive for V-valued samples.
- Keep recording gaps separate from transform padding; non-finite input is
  rejected and never repaired by this primitive.
- Register the primitive in the scientific package without changing the
  existing PSD/spectrogram numerical contracts.
- Add independent numerical and contract tests.

## Out of scope

- Changing the existing `offline-spectral-v3` PSD or `spectrogram-v2` output.
- Adding UI controls or frontend-side scientific calculations.
- Making arbitrary user-defined algorithms executable.

## Follow-up

The next change will expose PSD and STFT as official Runtime modules using
this primitive and the existing spectral quality gateway.
