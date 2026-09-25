# Dynamic Spectral Series Design

## Coordinate contract

The planner emits recording-relative half-open sample ranges:

```text
[start_sample, end_sample)
```

The corresponding display seconds are derived from the validated recording
sampling rate. A window's `window_start_s` and `window_end_s` are display
metadata, never the source of scientific indexing. The planner must reject
non-positive window or step sizes, ranges outside the recording, and sampling
rate changes within one Run.

## Result shape

Dynamic spectral output is a structured matrix series, not a scalar series:

```text
PSD:  [window, frequency]
STFT: [window, time_center_within_window, frequency]
```

The frequency axis is shared by all windows in one Run. STFT's within-window
time axis is relative to the dynamic window; the outer window axis carries the
recording-relative sample range. A channel dimension is included when a module
is configured for multiple channels; channel order is explicit metadata.

Internal units remain SI: EEG `V`, PSD/STFT linear power `V^2/Hz`, frequency
`Hz`, and time `s`. Display dB values retain their declared reference unit and
are never silently relabeled as SI linear power.

## Window states

Each planned window has exactly one state:

- `Partial`: the window is intentionally incomplete during a live dynamic run
  and has enough samples for a provisional calculation.
- `Complete`: the full requested window is present and passes its module
  quality gate.
- `Rejected`: samples are present but the quality or legality gate rejects the
  calculation.
- `Unavailable`: the requested window cannot be calculated because samples are
  missing, disconnected, or otherwise unavailable.

Recording gaps and transform padding remain separate evidence. A gap never
becomes zeros. Mathematical transform padding may be recorded only in the
transform-padding evidence for that window.

## Artifact and API boundary

The Run summary contains only contract version, output kind, axes metadata,
matrix shape, units, window count, state counts, requested/actual range,
provenance, and immutable artifact identity/checksum. Matrix values and full
window evidence are stored in the artifact. API result endpoints expose the
backend-produced arrays or artifact download; the frontend never recomputes
PSD, STFT, dB, or quality.

## Module semantics

PSD computes one frozen Welch estimate per planned window. A window with a
quality-gate failure produces an unavailable matrix row and a structured
reason; it does not contribute a fabricated zero spectrum.

STFT computes the frozen spectrogram contract inside each planned window. Its
inner time centers are relative to that window and its outer window coordinate
is recording-relative. A one-window STFT result must match the current static
STFT result after axis normalization.

Dynamic modes are enabled only after the module's parameter schema declares a
supported window and step policy. Static module behavior is unchanged.
