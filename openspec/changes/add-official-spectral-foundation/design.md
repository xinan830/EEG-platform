# Official algorithm development design

## Delivery order

```text
Contract freeze
    -> reusable scientific primitives
    -> static/dynamic Runtime result contracts
    -> official PSD module
    -> official STFT module
    -> artifact/provenance integration
    -> independent validation
    -> archive
```

## Ownership map

```text
backend/app/scientific/
    Mathematical primitives, units, quality, and evidence.

backend/app/algorithm_runtime/
    Module contracts, registry, validation, execution, and window scheduling.

backend/app/algorithms/<id>/
    Official algorithm manifest, config, parameter schema, computation, and
    module-specific evidence.

backend/app/services/
    Run orchestration, queueing, persistence, and API response assembly only.

backend/app/persistence/
    Repositories, migrations, and immutable artifact storage.

frontend/
    Configuration and rendering only; no FFT, PSD, band integration, or dB
    recomputation.
```

## Scientific rules

- FFT is an implementation primitive, not a selectable official algorithm.
- PSD and STFT outputs use internal SI units and declare display conversions
  explicitly at the API/export boundary.
- Sample ranges are authoritative; seconds are derived display/provenance
  values.
- Recording gaps are never filled to make a chart look continuous.
- Transform padding is permitted only where the primitive contract declares it
  and must be recorded separately.
- A failed scientific result is unavailable (`null`) with structured reasons,
  never a fabricated zero.

## Structured output direction

Scalar algorithms may continue using the scalar result contract. PSD and STFT
must use a structured result contract because they contain multiple fields:

```text
PSD:
  frequency_hz[]
  values[channel][frequency]
  unit = V^2/Hz
  channel_order
  actual_sample_range
  quality/evidence

STFT:
  time_center_s[]
  frequency_hz[]
  values[channel][time][frequency]
  linear_unit = V^2/Hz
  display_unit = dB re 1 V^2/Hz
  matrix_shape
  quality_rows
```

Large arrays belong in immutable NPZ artifacts with checksums. Run summaries
contain shape, units, axes metadata, and artifact identity rather than copies
of the entire matrix.

## Archived evidence

The following archived changes provide implementation evidence but are not new
backlogs:

- `2026-09-25-restructure-scientific-backend-boundaries`
- `2026-09-25-add-peak-frequency-band-ratio`
- `2026-09-15-enable-official-iapf-theta-beta`
- `2026-09-16-enable-rbp-faa-brainbeat`
- `2026-09-25-add-official-algorithm-parameter-contract`
