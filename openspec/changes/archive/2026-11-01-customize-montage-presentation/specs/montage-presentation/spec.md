## ADDED Requirements

### Requirement: Shared color selection
The desktop SHALL use one business-independent color picker for event and montage
editing, with two-way RGB color binding, preset swatches, HEX entry, RGB preview,
apply/cancel actions and a compact row mode.

#### Scenario: Cancel a custom color
- **WHEN** a user previews another RGB color and cancels
- **THEN** the bound saved draft color remains unchanged

### Requirement: Per-output montage appearance
Each montage output SHALL support a persisted optional RGB color and unique
nonnegative display order independent of physical source order. Reordering SHALL
preserve each output's color, electrode mapping and reference formula.

#### Scenario: Exchange two display rows
- **WHEN** a user moves F7-REF above F3-REF
- **THEN** acquisition and review show F7-REF first with its saved color
- **AND** F7 and F3 still resolve to their original native source inputs

### Requirement: Appearance snapshots and historical compatibility
Acquisition SHALL persist montage appearance in its existing immutable snapshot.
Review SHALL default to that snapshot and SHALL NOT substitute a later preset.
Legacy outputs without colors SHALL use the shared default waveform palette.
Complete profile fingerprints SHALL include explicit colors while preserving
legacy no-color fingerprint behavior and channel compatibility rules.

#### Scenario: Preset changed after acquisition
- **WHEN** a user edits a preset color after a recording completes
- **THEN** that recording's default review retains its acquisition color and order

### Requirement: Focused montage editor
The montage editor SHALL retain source and reference controls, remove the redundant
negative input-number column, and expose compact color and up/down order controls.
System presets SHALL remain read-only.

#### Scenario: Inspect a system preset
- **WHEN** a user opens a system montage
- **THEN** its colors and order are visible but cannot be changed without copying
