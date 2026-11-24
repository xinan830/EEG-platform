# analysis/offline-scientific-cache Specification

## ADDED Requirements

### Requirement: Separate execution mode from scientific identity

The system SHALL record `full` or `on_demand` as execution provenance and SHALL derive a common versioned scientific identity from verified source, sampling metadata, normalized algorithm configuration, scientific and implementation versions, published Definition digest, signal mapping/reference, filters, quality and window contracts. Execution mode and display state SHALL NOT change this identity.

#### Scenario: Resolve the same PSD configuration through both APIs

- **WHEN** full and on-demand PSD use the same recording and scientific inputs
- **THEN** they expose the same scientific cache key and their distinct execution modes
- **AND** a changed signal mapping, filter, implementation, window policy or source produces a different key.

### Requirement: Reuse verified on-demand PSD windows in a full analysis

A full PSD analysis SHALL reuse available ready on-demand windows only within its project and recording scope and with matching scientific identity and exact half-open sample range. It SHALL calculate missing windows with the same algorithm module and produce a complete independent artifact with reuse provenance.

#### Scenario: Some windows have been explored

- **WHEN** a user starts a full PSD analysis after viewing some on-demand windows
- **THEN** verified matching windows are reused and remaining windows are calculated
- **AND** the full result retains every planned window and normal progress/cancellation behavior.

#### Scenario: Cached payload is damaged or unverified

- **WHEN** metadata, checksum, identity, unit or shape verification fails or a legacy window lacks scientific identity
- **THEN** the full analysis records a cache miss reason and computes the window
- **AND** it does not modify the historical payload or report the damaged output as valid.

### Requirement: Prove independent PSD execution equivalence

Validation SHALL compare independent full and on-demand PSD execution for identical inputs, sample ranges, frequency axes, units, quality and evidence using explicit numerical tolerances. A comparison that simply reads a reused window SHALL NOT establish numerical equivalence.

#### Scenario: Validate fresh PSD paths

- **WHEN** both modes execute without cross-mode reuse
- **THEN** frequency coordinates and quality evidence match and numeric PSD values satisfy the declared tolerance
- **AND** execution mode remains distinguishable while scientific identity is the same.
