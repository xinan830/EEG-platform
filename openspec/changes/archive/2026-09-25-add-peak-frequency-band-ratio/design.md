# Design

## Module identity

The catalog exposes two stable IDs:

```text
peak_frequency
band_ratio
```

They are separate from the existing `iapf`, `rbp`, and `theta_beta` IDs. A
module manifest declares its scientific version, implementation identity,
parameter schema, supported modes, input roles, output schema, quality policy,
and availability. The generic Run service dispatches through the closed Runtime
registry and does not branch on these IDs to implement science.

## Canonical input and units

The input is a resolved PSD produced from EEG values in `V`/float64. PSD values
use `V^2/Hz`; integrated band power uses `V^2`; frequency uses `Hz`; durations
use `s`. Any microvolt display conversion is explicit at the API or frontend
boundary and is not part of the scientific primitive.

Every request resolves to a recording-relative half-open sample range:

```text
[start_sample_inclusive, end_sample_exclusive)
```

Seconds submitted by the UI are resolved once using the validated sampling rate
before cache identity, window construction, event alignment, and execution.
The resolved range and derived seconds are persisted in evidence.

## PeakFrequency semantics

`PeakFrequency` accepts one explicit frequency band `[low_hz, high_hz]` and one
or more explicitly ordered source channels. The primitive selects the maximum
PSD value on the declared frequency grid within the band. Tie handling, edge
inclusion, interpolation policy, and insufficient-bin behavior are versioned
and recorded; no channel or frequency is inferred from position or name.

The output is a scalar `peak_frequency_hz` plus evidence containing the selected
bin, peak power, resolved band, frequency grid, channel order, and quality. A
missing or rejected result is `null` with a structured reason, never zero.
The module must not label this output as Alpha or IAPF unless a separate IAPF
module is executing its own Alpha-specific contract.

## BandRatio semantics

`BandRatio` accepts two explicitly declared bands: numerator and denominator.
Each band is integrated with the authoritative band-power interpolation and
boundary policy. The output is:

```text
numerator_power / denominator_power
```

with dimensionless `ratio` units. Zero or unavailable denominator power produces
an unavailable result with a stable reason; it never produces infinity, NaN, or
zero as a substitute. The generic module may permit mathematically valid
overlap, while an official preset may impose semantic restrictions and must
persist the resolved bands. It must not be presented as the standard
Theta/Beta preset unless that preset's exact contract is selected.

## Static, dynamic, and box selection

Static mode executes one resolved sample range and returns one result. Dynamic
mode resolves one analysis range, sample-aligned window length, and sample-
aligned step. Each point includes requested and actual ranges, anchor semantics,
valid/rejected counts, gap state, quality, and the module parameter snapshot.
An incomplete but usable first or last window is `Partial`; invalid or failed
windows use `Rejected` or `Unavailable` with reasons.

UI box selection creates a static request from the selected sample bounds and
uses the same Runtime, cache identity, provenance, and quality path. It is not a
third scientific implementation.

## Quality and gaps

Shared quality layers report `DataIntegrity`, `SignalQuality`, and
`AlgorithmValidity`. Recording gaps are never imputed. Transform padding, if
the PSD contract permits it, is separately identified as transform padding in
evidence. Non-finite values, insufficient frequency bins, invalid bands,
flatline/clipping failures, and zero denominator are explicit reason codes.

## Provenance and output schema

Each Run stores the module ID, scientific version, implementation identity,
complete validated parameter snapshot, configuration hash, requested and actual
sample ranges, sampling rate, channel order, preprocessing snapshot, PSD and
band-power primitive versions, resolved bands, quality evidence, and output
schema. Output fields are individually typed and unit-bearing; a single module
output unit is not sufficient for evidence axes or multi-field results.

## Validation and rollback

Before implementation is considered complete, each module must pass frozen
golden tests and an independent NumPy reference that reads the raw fixture
separately. Tests must include a shifted-coordinate failure and a tolerance
failure so evidence checks cannot silently compare the wrong range or relaxed
values. A module can be disabled in the catalog without deleting its code or
historical results. Rollback is a code/catalog rollback; existing raw data and
Runs remain readable.
