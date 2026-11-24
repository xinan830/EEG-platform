## ADDED Requirements

### Requirement: Present analysis notch as an algorithm-owned setting

The desktop SHALL expose the backend-declared spectral notch choice in the algorithm analysis configuration, default it to 50 Hz when declared by the current contract, and provide an explicit off choice. It SHALL serialize that choice with the algorithm run request. The control SHALL remain independent of review waveform filter controls and SHALL NOT alter display filtering.

#### Scenario: Submit with the default notch
- **WHEN** the user submits a spectral algorithm without changing its analysis notch setting
- **THEN** the request carries the declared 50 Hz default.

#### Scenario: Submit with notch disabled
- **WHEN** the user selects the explicit off option
- **THEN** the request carries a null/off value
- **AND** the display waveform filter setting remains unchanged.
