# WPF Official Algorithm Integration Design

## Ownership

```text
WPF: device lifecycle, raw chunks, recording manifest, interaction, display
Python: Recording registration, sample reading, algorithms, quality, Runs,
        artifacts, provenance
Vue: validation and engineering comparison only
```

WPF never writes backend SQLite and never writes scientific artifacts. The
backend reads a completed recording through a registered source adapter.

## Vertical slice

The first executable slice is:

```text
completed WPF recording
  -> POST recording registration
  -> GET official catalog
  -> POST static PSD Run
  -> poll Run
  -> GET structured preview
  -> render result in WPF
```

The same typed transport layer must support all currently runnable official
modules after the PSD slice is verified: STFT, IAPF, RBP, FAA, Theta/Beta,
Peak Frequency, and Band Ratio.

## Recording registration

Registration is idempotent by the immutable manifest/source identity. The
request includes the completed manifest path or a local recording source
descriptor, manifest SHA-256, recording start UTC, sampling rate, actual
channel order and units, sample-counter segments, and explicit gap records.
The backend validates that all referenced chunks exist, are complete, and
decode as sample-major float64 V data before returning `recording_id`.

No wall-clock receive time is used as the EEG sample axis. Paused intervals and
counter discontinuities remain gaps. Registration failure does not mutate raw
files and does not create an Analysis Run.

## WPF transport models

WPF models are explicit DTOs, separate from view models. Unknown backend
algorithm parameters are rendered as unavailable until a supported parameter
editor exists; WPF must not infer meaning from a parameter key.

Run polling is cancellable and bounded. Terminal states are `completed`,
`gate_failed`, `failed`, and `cancelled`. Structured preview values are
displayed as returned; JSON `null` remains unavailable and is never changed to
zero.

## Result presentation

The algorithm workspace is a separate WPF module. It may be opened from a
completed recording/review context, but it does not own waveform playback or
acquisition. It displays algorithm identity/version, requested and actual
ranges, channel order, units, quality state, error code, and result values.
Charts consume backend axes and matrices without recalculating them.

## Alternatives rejected

- Calling Python modules directly from WPF: breaks process and provenance
  boundaries.
- Reusing the Vue workbench as the product UI: contradicts the WPF product
  boundary and leaves acquisition/review context split.
- Copying algorithm formulas into C#: creates two scientific authorities.
- Treating a live batch as a completed Recording: would mix streaming and
  offline Run semantics; real-time algorithms need a separate future change.

## Verification

Use fixture recordings with known manifest hashes, missing chunks, explicit
gaps, and valid PSD/STFT artifacts. Verify backend contract tests, WPF DTO and
client tests, WPF build/test, and a manual WPF static PSD round trip. Compare
the WPF-displayed values with the backend preview payload, not with a second
frontend calculation.
