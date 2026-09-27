# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: WPF provides standard frequency-band presets without hiding exact values

WPF SHALL provide standard band presets for Peak Frequency and Band Ratio,
populate the existing numeric inputs when selected, and submit the resulting
numeric values. Editing a populated value SHALL mark the selection as Custom.

#### Scenario: Preset selection and manual override

- **WHEN** the operator selects Alpha for Peak Frequency or Theta/Beta for Band
  Ratio
- **THEN** WPF fills the corresponding numeric fields, and after any manual
  edit displays Custom while preserving the edited values for validation and
  submission

### Requirement: Fixed official definitions remain distinct from generic presets

WPF SHALL NOT expose frequency preset editing as a replacement for the fixed
IAPF or official Theta/Beta definitions.

#### Scenario: IAPF remains fixed

- **WHEN** the operator selects IAPF
- **THEN** no generic band preset is shown and the backend IAPF contract remains
  unchanged
