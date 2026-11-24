# Acquisition preview montage switch

## Purpose

Allow users to inspect compatible montage projections during live preview without changing the recording snapshot or scientific input.

## ADDED Requirements

### Requirement: Preview-only display montage selection

The desktop SHALL expose a transient live display montage selection separate from the selected acquisition montage. It SHALL allow changing the display selection only while the acquisition runtime is in `Previewing` and SHALL apply the selection through the existing live display projection.

#### Scenario: Switch display montage during preview

- **GIVEN** preview was prepared with montage A and montage B uses the same channel configuration fingerprint
- **WHEN** the user selects montage B in the live acquisition toolbar
- **THEN** the live waveform uses montage B's derived outputs
- **AND** the raw stream, sample counters, timestamps, writer input and algorithm input remain unchanged

#### Scenario: Reject an incompatible display montage

- **GIVEN** the selected acquisition montage and a candidate display montage use different channel configuration fingerprints
- **WHEN** the candidate is assigned to the live display selection
- **THEN** the assignment is rejected and the previous display selection remains active

### Requirement: Recording snapshot remains frozen

The desktop SHALL initialize the live display selection from the acquisition montage snapshot when preview starts, but SHALL NOT replace the runtime recording montage snapshot when the display selection changes. The live display selector SHALL be disabled while recording, paused, stopping or outside preview.

#### Scenario: Record after switching the preview display

- **GIVEN** preview was prepared with montage A and the user switched the display to compatible montage B
- **WHEN** recording starts
- **THEN** the recording manifest and raw data retain montage A's snapshot
- **AND** the live display remains on B only for the current preview/recording surface
- **AND** no API or algorithm contract changes

### Requirement: Explicit user-facing labels

The data information surface SHALL label the transient selection as “实时显示导联” and the frozen acquisition choice as “录制入库导联”.

#### Scenario: Inspect acquisition information

- **WHEN** the user opens the acquisition data information panel
- **THEN** both display and recording montage names are shown separately
- **AND** the panel explains that switching the display does not modify recorded raw data
