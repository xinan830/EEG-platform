# desktop/wpf-official-algorithms Specification

## ADDED Requirements

### Requirement: The static PSD form uses explicit project recording and analysis inputs

WPF SHALL select a completed recording from the project catalog and register it
before enabling a static PSD Run. The operator SHALL explicitly choose one
backend-declared EEG channel and requested start/end range. WPF SHALL reject
empty, non-finite, reversed, or out-of-recording ranges before submission.

#### Scenario: The operator submits static PSD

- **WHEN** a completed project recording is registered and the operator chooses PSD, an available channel, and a valid requested range
- **THEN** WPF submits those exact inputs and the selected scientific version to the backend
- **AND** no other official algorithm is submitted using the PSD-only form

### Requirement: The PSD result is a rendering of backend output

The WPF PSD result view SHALL display the backend-returned frequency axis,
power-density unit, and available values. Null or non-finite preview cells
SHALL remain unavailable and SHALL NOT be replaced by zero.

#### Scenario: A completed PSD Run has a structured preview

- **WHEN** the backend returns a bounded static frequency series
- **THEN** WPF draws only the returned points with explicit axis labels and units
- **AND** requested and actual ranges and Run identity remain visible
