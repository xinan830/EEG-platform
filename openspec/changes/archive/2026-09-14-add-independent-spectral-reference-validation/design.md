## Design

`IndependentSpectralReferenceService` receives a recording, a closed time
range, and channels.  It asks `RecordingService` for source samples solely to
read the recording, then separately implements the locked analysis contract:

1. select samples by integer time indices;
2. zero-phase 1--30 Hz fourth-order Butterworth SOS filtering;
3. 4 s Hann Welch segments with 2 s overlap, constant detrending and density
   scaling;
4. retain 1--30 Hz bins and convert V2/Hz to uV2/Hz once at the boundary.

The service obtains the production comparison values through the existing
`load_spectrum` API path.  The independent reference module never imports or
calls `preprocess_offline`, `estimate_welch_psd`, or `band_power`.

The comparison is flattened in requested-channel then ascending-frequency
order.  `ValidationService` persists an optional JSON evidence object with
frequency coordinates and the two PSD arrays.  It contains derived algorithm
output only, not samples or source filenames.  The existing report and export
routes expose that evidence without a second data format.

The endpoint returns the completed validation record and its pointwise
evidence.  Failed quality gates remain unavailable (`null`) rather than being
converted to zeros.  All reports retain the engineering-only scope label.

## Tolerances

Default tolerances are `rtol=1e-7` and `atol=1e-9 uV^2/Hz`, the same PSD
comparison contract used for official algorithm shadow checks.  They are an
engineering agreement threshold, not clinical acceptance criteria.

## Safety and provenance

Dataset identity contains recording ID, source SHA-256, and byte size.  The
configuration digest covers actual sample-derived range, channel order,
filter, Welch settings, units, and reference implementation identity.
