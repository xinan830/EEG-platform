## Context

Current algorithms pass NumPy arrays and dictionaries directly. Change 03 needs deterministic type/unit checking, but this change must not introduce graph persistence or switch official result paths.

## Goals / Non-Goals

**Goals:** immutable scientific value wrappers, explicit units/dimensions, a closed node registry, deterministic windows, anti-aliased resampling, quality propagation, and testable RBP/FAA composition.

**Non-Goals:** graph execution, API persistence, user formulas, frontend calculation, or official algorithm migration.

## Decisions

- Use frozen dataclasses with NumPy arrays marked read-only. Alternative mutable dictionaries are rejected because nodes could silently corrupt shared inputs.
- Model units as enum values plus dimension rules. Addition/subtraction requires identical units; ratios of identical dimensions are dimensionless; logarithm accepts positive scalar/power values and returns dimensionless. Automatic V/uV scaling is rejected.
- Use dedicated immutable value types (`EEGSignal`, `WindowedSignal`, `PSDSeries`, `BandPower`, `RelativePower`, `Scalar`, and `TimeSeries`) with shared provenance and quality types. This makes each shape and unit contract locally auditable while keeping node signatures explicit; an envelope can be introduced later only if graph execution demonstrates a concrete need.
- Store time as absolute `start_s`, `sfreq_hz`, and explicit window start/end/center arrays. Residual policy is `drop` or `reject`; no implicit padding.
- Use `scipy.signal.resample_poly` with rational up/down factors as the declared anti-alias method.
- Register node definitions in a closed mapping from stable node type to callable plus accepted kinds. No import path, eval, lambda source, or dynamic Python is accepted.
- Quality masks carry status, reasons, and optional per-window booleans. Nodes combine bad reasons; gates return `available=False` rather than numeric zero.

## Risks / Trade-offs

- **[One envelope can be less expressive than dedicated classes]** -> validate shape/metadata by kind in constructors and tests; split only if real divergence appears.
- **[Unit algebra can grow complex]** -> implement only the locked unit set and explicit operations required now.
- **[Resample ratios can become large]** -> bound denominator and record realized sampling rate.
- **[Primitive Welch could drift from v3]** -> call the existing pure spectral function where its fixed contract applies and compare outputs.

## Migration Plan

Add modules and tests without wiring official APIs. Document contracts, validate, archive, then let Change 03 consume the registry. Rollback deletes only additive modules; no persisted data changes.

## Testing Strategy

Test immutability, shapes, channel order, every unit rejection, window centers/residuals, resampling alias suppression, quality propagation, node allowlist, RBP sum, FAA formula, and parity with current Welch output.
