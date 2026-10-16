# analysis/offline-stft-scientific-cache Specification

## Purpose
TBD - created by archiving change share-stft-offline-scientific-window-identity. Update Purpose after archive.
## Requirements
### Requirement: Normalize STFT inner time coordinates

The system SHALL represent every complete dynamic STFT window with an inner
time axis relative to that outer window's start, using the declared 4-second
segment and 1-second step contract. The axis SHALL NOT depend on the recording's
absolute start position.

#### Scenario: Same STFT window at a later recording position

- **WHEN** a complete outer window starts at 10 seconds
- **THEN** its inner time coordinates remain the same relative coordinates as
  a window starting at 0 seconds
- **AND** the outer window range remains the recording-relative provenance.

### Requirement: Reuse only complete verified STFT windows

A full dynamic STFT analysis SHALL reuse an on-demand window only when its
scientific identity, exact half-open sample range, output schema, units,
frequency axis, relative inner time axis, matrix shape, quality and complete
analysis state all match the current module contract. Partial, rejected,
unavailable, legacy or unverified windows SHALL be recalculated by the same
STFT module.

#### Scenario: Warm-up window exists

- **WHEN** an on-demand warm-up window has fewer than the complete inner rows
- **THEN** it remains available for interactive display
- **AND** a later full Run does not reuse it as a complete matrix row.

### Requirement: Preserve independent STFT result provenance

The full STFT artifact SHALL remain complete and independently persisted even
when some windows are reused. Its provenance SHALL distinguish `full` execution
from `on_demand` source windows and SHALL record reuse counts and miss reasons.

#### Scenario: Reuse and recompute in one Run

- **WHEN** one complete window is cached and another planned window is absent
- **THEN** the cached matrix is included and the missing window is computed by
  the STFT module
- **AND** every planned window remains represented with its actual range/state.

