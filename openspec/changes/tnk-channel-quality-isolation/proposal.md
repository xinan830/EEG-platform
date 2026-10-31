# Isolate TNK channel quality failures

TNK currently prewarms one shared spectrum for all mapped channels. A quality
failure in one channel therefore rejects every spectral child and leaves a
group-wide blank interval, even when the other channels are valid.

Change the TNK shared-spectrum fallback to evaluate each mapped channel with
the existing quality gate when the combined request is rejected. Valid channels
continue to produce scientific values; only children that require a rejected
channel remain unavailable with the structured quality reason.

## Scope

- Keep the existing shared multi-channel fast path for clean windows.
- On a shared quality rejection, retry the same bounded window per channel.
- Compose the valid channel spectra and preserve rejected-channel evidence.
- Reject only child calculations that consume a rejected channel.

## Non-goals

- Do not lower or remove the 150 µV quality threshold.
- Do not impute, interpolate, zero-fill, or repeat previous scientific values.
- Do not change waveform playback, cache identity, window cadence, or formulas.
