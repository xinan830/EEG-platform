# Custom spectrogram band trend

Custom frequency ranges now produce a backend-computed power trend instead of falling back to the four standard EEG bands.

## Contract

- Request: `custom_frequency_range: { low_hz, high_hz }` on configured Spectrogram requests only.
- Bounds: `1 <= low_hz < high_hz <= 30`.
- For each 4-second Spectrogram window, the backend integrates the linear PSD over the requested range with trapezoidal integration and interpolated range boundaries.
- Output: `custom_band_power_timeseries[channel]`, unit `uV^2`.
- Traceability metadata is returned in `custom_band`, including frequency resolution, included frequency points, integration rule and `spectrogram-custom-band-v1`.
- Bad quality windows remain non-finite and are not connected across by the frontend.
- The algorithm-validation dialog displays the custom range, integration rule, units, frequency resolution, participating bins and custom trend version.

The frontend does not integrate PSD values. `offline-spectral-v3`, the standard Delta/Theta/Alpha/Beta trends and the complete Spectrogram matrix are unchanged.
