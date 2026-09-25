# analysis/dynamic-spectral-series Specification

## Purpose
TBD - created by archiving change add-dynamic-spectral-series. Update Purpose after archive.
## Requirements
### Requirement: Represent dynamic spectral matrices as typed series

The backend SHALL represent dynamic PSD and STFT output as a structured matrix
series with explicit recording-relative window sample ranges, shared axes,
channel order, units, matrix shape, per-window quality state, and provenance.
It SHALL NOT encode a matrix series as `AlgorithmSeriesResult` scalar values.

#### Scenario: PSD dynamic output

- **WHEN** a valid dynamic PSD Run completes
- **THEN** its linear matrix has a declared `window x frequency` shape, a shared
  frequency axis in Hz, PSD units of `V^2/Hz`, and one state/evidence row per
  planned window

#### Scenario: STFT dynamic output

- **WHEN** a valid dynamic STFT Run completes
- **THEN** its matrix has a declared `window x inner_time x frequency` shape,
  an inner time axis in seconds relative to each window, and a recording-
  relative sample range for each outer window

### Requirement: Preserve canonical window states and evidence

Every dynamic spectral window SHALL have exactly one state from `Partial`,
`Complete`, `Rejected`, or `Unavailable`. Recording gaps SHALL remain distinct
from transform zero-padding, and unavailable or rejected windows SHALL NOT be
replaced with numeric zero matrices.

#### Scenario: Gap in one window

- **WHEN** a planned window contains missing or non-finite recording samples
- **THEN** the result records a gap reason and unavailable/rejected state for
  that window, while neighboring valid windows remain independently usable

### Requirement: Persist matrices outside Run summaries

The backend SHALL persist dynamic spectral matrices and full window evidence as
immutable checksum-verified artifacts. Run summaries and API metadata SHALL
contain only bounded shapes, units, axes metadata, state counts, provenance,
requested/actual ranges, and artifact identity.

#### Scenario: Artifact-backed result

- **WHEN** a client reads a completed dynamic spectral Run
- **THEN** it can retrieve the backend-produced arrays from the artifact and
  does not need to recompute scientific values in the frontend

### Requirement: Preserve static compatibility

Enabling dynamic matrix-series execution SHALL NOT change the scientific shape,
units, provenance, or artifact readability of existing static PSD/STFT Runs.

#### Scenario: One-window equivalence

- **WHEN** the same recording range is evaluated as a static Run and as a
  one-window dynamic Run
- **THEN** their normalized scientific matrices and axes match within the
  declared numerical tolerances

