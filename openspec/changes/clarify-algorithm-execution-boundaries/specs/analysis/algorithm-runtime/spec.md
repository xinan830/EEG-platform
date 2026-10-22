## ADDED Requirements

### Requirement: Resolve official algorithm inputs through one typed signal boundary

Every official algorithm SHALL receive recording-backed signal data through one typed runtime boundary. The boundary SHALL define V/float64 values, sample-major shape, channel names and order, sampling rate in Hz, absolute sample/time range, unavailable-source behavior, missing-sample semantics, selected montage identity, and preprocessing provenance. Algorithm modules SHALL declare the signal roles and preprocessing they require; the boundary SHALL apply only the declared projection and preprocessing and SHALL NOT own algorithm formulas.

#### Scenario: Run with a selected montage and filter
- **WHEN** an official algorithm requests declared source roles under a selected montage and filter contract
- **THEN** it receives only the resolved outputs in the declared role order as V/float64 data
- **AND** the input evidence identifies the montage snapshot, filter contract, sampling rate, sample bounds, units, and source channel mapping

#### Scenario: Offer official presets from recorded channels
- **WHEN** a completed recording contains an official ANT channel-configuration snapshot and the user montage store has no official presets
- **THEN** the shared recording montage catalogue generates official preset candidates from that recorded snapshot, without depending on current device mappings or user profile persistence
- **AND** it includes only candidates passing the existing recorded-source, channel-order, unit, hardware-reference and mapping compatibility checks
- **AND** algorithm registration sends compatible alternatives through backend validation and immutable analysis snapshots, without changing raw recording files

#### Scenario: Required source is unavailable
- **WHEN** a requested role, sample range, or preprocessing operation cannot be resolved
- **THEN** the runtime returns a structured unavailable or failed result
- **AND** it does not substitute another channel, use a different preprocessing contract, or return fabricated values

#### Scenario: Algorithm requires a distinct preprocessing scope
- **WHEN** an algorithm declares continuous-before-slicing, per-window, or no software filtering semantics
- **THEN** the shared boundary executes that declared scope and records it
- **AND** the algorithm module retains ownership of its scientific calculation and algorithm-specific quality rules

### Requirement: Use only canonical current dynamic result state

Every current dynamic result SHALL provide an explicit canonical state for every point or structured window. The runtime SHALL reject missing or mismatched state data rather than deriving current state from deprecated warmup or quality compatibility fields. Current clients and APIs SHALL consume the canonical state and current result contract only.

#### Scenario: Dynamic module returns complete state data
- **WHEN** a module returns one state for each result point or window
- **THEN** the runtime validates and persists those states with the result contract version

#### Scenario: Dynamic module omits canonical state
- **WHEN** a module omits a state or returns a state count that differs from its points/windows
- **THEN** validation fails with a structured execution error
- **AND** the runtime does not infer state from legacy warmup flags or quality strings

## MODIFIED Requirements

### Requirement: Produce dynamic results from one frame contract

The system SHALL plan every dynamic algorithm point through one backend-owned,
endpoint-aligned `DynamicAnalysisFrame` contract. Each returned point SHALL
state its actual range, explicit canonical analysis state, result-contract version, quality, and
available spectral provenance. Algorithm modules SHALL NOT independently
invent time-window scheduling or load a configured duration that differs from
the point's actual duration.

#### Scenario: Return an early warm-up point

- **WHEN** playback reaches 8 s with a configured 10 s dynamic analysis
  window and the algorithm declares partial-window support
- **THEN** the system returns one `Partial` point for exactly `0–8 s`, anchors it
  at `8 s`, and loads only that real EEG range
- **AND** the point explicitly reports its quality and state without a deprecated warmup flag

#### Scenario: Reuse a dynamic result cache

- **WHEN** a persisted dynamic-result evidence contract has changed
- **THEN** the system does not reuse a cached result produced under the old
  contract version
