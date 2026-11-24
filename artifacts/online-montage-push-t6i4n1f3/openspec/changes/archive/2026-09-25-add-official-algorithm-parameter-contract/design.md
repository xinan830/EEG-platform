# Design

## Layering

The backend owns this pipeline:

```text
Recording
  -> input/channel selection
  -> preprocessing and quality mask
  -> scientific primitives
  -> official algorithm module
  -> static or dynamic execution
  -> result, evidence, and provenance
```

FFT is an implementation primitive, not automatically a user-facing
algorithm. The first stable scientific primitive contracts are Welch PSD, band
power, peak frequency, and STFT time-frequency output. Sample-aligned sliding
windows are execution primitives shared by static and dynamic Runs, not
scientific output primitives.

`AlgorithmParameter` remains the single backend-owned description consumed by
the catalog. Numeric constraints are optional because channel and mode fields
do not use them.

## Units and coordinates

Internal scientific values use SI units: EEG is `V`/float64, PSD is `V^2/Hz`,
band power is `V^2`, frequency is `Hz`, and time is `s`. Any `uV`-based API or
export representation requires an explicit, recorded conversion at the
boundary; primitives never apply an implicit scale.

The canonical scientific coordinate is the recording-relative sample index.
All ranges use a half-open interval:

```text
[start_sample_inclusive, end_sample_exclusive)
```

Seconds are derived from the sample coordinate and the validated sampling rate.
Requests may accept seconds for UI convenience, but they must be resolved to
sample indices before cache identity, windowing, event alignment, or execution.

## Validation order

Raw requests are parsed first. Shared schema validation then checks types, enum
values, numeric bounds, units, and basic mathematical legality. Module or
official-preset semantic validation runs only after shared validation and
before input resolution or EEG loading.

Enum validation uses declared option values. Numeric validation uses inclusive
`minimum`/`maximum`; `step` is metadata for clients and is validated from the
declared origin (`minimum`, or zero when no minimum exists) with a small
floating-point tolerance.

The change is additive. Rollback is a code rollback; old clients continue to
work because they already send the same configuration fields. Presets are
stored in a separate SQLite table and are never referenced as the source of
truth for a Run; a Run receives a copied, validated configuration.

## Scientific boundaries

- A generic engine enforces mathematical legality: ordered positive bands,
  Nyquist limits, valid windows, sample availability, and compatible units.
- An official preset enforces semantic rules: for example, standard
  Theta/Beta bands and the IAPF Alpha meaning.
- Quality is layered: shared checks cover gaps, non-finite values, amplitude,
  clipping, and flatline; each algorithm may add its own quality gate.
- Static execution consumes one canonical sample range. Dynamic execution
  consumes one canonical analysis range plus sample-aligned window and step and
  returns actual sample bounds, derived time bounds, anchor, valid/rejected
  counts, gap state, and quality per point. UI box selection creates a static
  request and no third execution path.
- A dynamic analysis window is `Partial`, `Complete`, `Rejected`, or
  `Unavailable`. `Partial` replaces the ambiguous analysis-window use of
  `warm-up`; filter state initialization remains separately named
  `FilterWarmup`. Existing persisted `warmups` fields require a new
  result-contract version before being renamed.
- Recording-gap imputation and transform zero-padding are distinct. A gap must
  never be filled with fabricated EEG; FFT/STFT zero-padding, when allowed by a
  primitive contract, is only a numerical transform option and is recorded as
  such.
- Quality is represented as `DataIntegrity`, `SignalQuality`, and
  `AlgorithmValidity`, with a stable status, reason list, and metrics object.
- Display configuration is separate from analysis configuration. Timebase,
  paper speed, sensitivity, colors, and chart history never affect algorithm
  cache identity or numerical results.
- A result stores the algorithm identity/version, implementation identity,
  parameter snapshot, input sample range, channel/montage snapshot,
  preprocessing snapshot, primitive versions, quality, units, and evidence.
  Outputs use an `output_schema` with one unit-bearing declaration per field;
  a single `output_unit` is insufficient for multi-value algorithms.
