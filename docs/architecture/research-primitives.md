# Research Primitives

## Scope

`backend/app/eeg_core/primitives` is a backend-only, closed set of typed
scientific building blocks. It is additive. It does not replace the current
Viewer Pipeline or produce results for the current public analysis endpoints.
`offline-spectral-v3` remains the official spectral contract until a later,
separately validated migration change switches a result path.

## Data contract

All primitive arrays are copied into immutable `float64` NumPy arrays. The
principal types are `EEGSignal`, `WindowedSignal`, `PSDSeries`, `BandPower`,
`RelativePower`, `Scalar`, `TimeSeries`, `ChannelMap`, and `QualityMask`.

- EEG input remains samples-by-channels and carries explicit absolute seconds,
  sampling rate, ordered source labels, and `V` or `uV` unit.
- A window carries each absolute `[start_s, end_s]` interval. Its center is the
  midpoint; a 10 second signal windowed as 4 seconds / 2 seconds has centers
  2, 4, 6, and 8 seconds.
- PSD is channels-by-frequency and must carry `V^2/Hz` or `uV^2/Hz`.
- Band power is channels-by-band with `V^2` or `uV^2`; relative power is a
  ratio. A failed value is `None`, never a numerical zero.
- Every node appends its resolved parameters to immutable provenance.

## Units and quality

Units are deliberately strict. Addition and subtraction require literally the
same unit; `V` and `uV` are not silently converted. A caller that needs a
conversion must request it at an explicit API or node boundary in a later
change. The current algebra supports only the products and divisions required
by the approved primitives. This limitation is intentional: it makes invalid
research graphs fail early rather than look plausible.

Quality reasons propagate. A bad Welch candidate is excluded and its reason is
retained in `rejected_reasons` even when enough candidates remain for an
available result. If the clean ratio is below the declared gate, the PSD and
all dependent results are unavailable with the original reasons plus
`low_quality` when applicable.

## Approved nodes

The `NODE_REGISTRY` is closed. It contains channel selection, rereference,
bandpass, notch, polyphase anti-aliased resampling, detrending, windowing,
Welch PSD, band power, relative power, quality gate, arithmetic, log,
statistics, weighted sum, and output marking. Unknown names fail before work
begins. No formula string, `eval`, dynamic import, user lambda, or arbitrary
Python is accepted.

Resampling uses `scipy.signal.resample_poly`, records its rational rate and
realized sample rate, and therefore states its FIR anti-alias method. Window
residuals are either explicitly dropped or rejected; they are never padded.

## Reference compositions

`fixed_band_rbp` constructs one band power divided by 1-30 Hz total power.
`frontal_alpha_asymmetry` constructs `ln(alpha F4) - ln(alpha F3)` from named
channels. The latter intentionally preserves the current `offline-spectral-v3`
PSD floor of `1e-20 V^2/Hz`; this can cause sub-nanounit differences from the
ideal amplitude-only logarithm in synthetic data.

## Verification boundary

The primitive Welch node is tested point-by-point against the existing pure
`estimate_welch_psd` implementation. This is production-consistency evidence,
not an independent scientific reference implementation. Existing independent
SciPy validation remains the authority for the `offline-spectral-v3` maths.
