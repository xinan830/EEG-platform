# acquisition-preview-montage-switch Specification

## Purpose

Allow users to inspect compatible montage projections during live preview without changing the recording snapshot or scientific input.

## Requirements

### Requirement: Preview-only display montage selection
The desktop SHALL expose one transient, versioned live montage selection separate from the selected acquisition montage. It SHALL allow changing the live selection only while the acquisition runtime is in `Previewing`, and SHALL apply that same immutable selection to waveform projection and supported live algorithm inputs at a sample-counter boundary.

#### Scenario: Switch display montage during preview
- **GIVEN** preview uses montage A and montage B has the same channel configuration fingerprint
- **WHEN** the user selects montage B
- **THEN** the waveform projection and supported live algorithm inputs use montage B and its ordered outputs from the same configuration revision
- **AND** live algorithm windows do not combine samples from montage A and montage B
- **AND** raw stream values, channel order, V units, sample counters, timestamps and writer input remain unchanged

#### Scenario: Reject an incompatible display montage
- **GIVEN** a candidate montage has a different channel configuration fingerprint or cannot resolve an algorithm's declared channel requirements
- **WHEN** the user selects the candidate for the live session
- **THEN** the candidate is rejected for the incompatible consumer with an explicit unavailable reason
- **AND** no consumer silently substitutes a previous or default channel mapping

#### Scenario: Mark an unsupported algorithm mapping unavailable
- **GIVEN** a compatible montage cannot resolve a live algorithm's declared channel input
- **WHEN** the montage is selected during preview
- **THEN** the affected algorithm is reported unavailable with an explicit reason while other compatible consumers can use the selected montage
- **AND** raw sample upload continues unchanged

### Requirement: Recording snapshot remains frozen
The desktop SHALL initialize the live montage selection from the acquisition montage snapshot when preview starts, but SHALL NOT replace the runtime recording montage snapshot when the live selection changes. The live montage selector SHALL be disabled while recording, paused, stopping or outside preview.

#### Scenario: Record after switching the preview display
- **GIVEN** preview was prepared with montage A and the user switched the live montage to compatible montage B
- **WHEN** recording starts
- **THEN** the recording manifest and raw data retain montage A's snapshot
- **AND** waveform and supported live algorithms remain on B only for the current acquisition session
- **AND** the offline review coordinator and public algorithm contracts remain unchanged

### Requirement: Explicit user-facing labels
The data information surface SHALL label the transient selection as “实时显示导联” and the frozen acquisition choice as “录制入库导联”.

#### Scenario: Inspect acquisition information
- **WHEN** the user opens the acquisition data information panel
- **THEN** both live and recording montage names are shown separately
- **AND** the panel explains that switching the live montage does not modify recorded raw data

### Requirement: Share a versioned live signal view
The acquisition session SHALL expose the selected montage identity, configuration revision, ordered channel names, sample coordinates and V-unit input contract to waveform and live analysis consumers. Each batch SHALL be interpreted against one captured revision.

#### Scenario: A batch is dispatched while selection changes
- **WHEN** a montage change overlaps acquisition batch dispatch
- **THEN** the batch and its waveform and algorithm work retain one complete revision and channel order
- **AND** a later batch uses the new revision after the declared sample-counter boundary

### Requirement: Reject live results from an obsolete montage revision
The acquisition workspace SHALL prevent results computed for an earlier live montage revision from replacing the active algorithm view after a montage change.

#### Scenario: An old montage result completes late
- **WHEN** a live algorithm result for montage revision N arrives after revision N+1 is active
- **THEN** the result is ignored or marked obsolete
- **AND** it cannot replace the visible result for revision N+1
