## MODIFIED Requirements

### Requirement: WPF submits a traceable offline Run

The WPF workspace SHALL submit an official Run only for a successfully registered Recording
and SHALL include the selected algorithm ID, exact scientific version, explicit source
channel roles, selected historical analysis montage identity, exact analysis output
channels, requested range, mode, and validated parameter values. The montage identity
and output mapping SHALL be part of the scientific configuration and SHALL NOT be inferred
from the display montage. Explicit current compatible profiles SHALL be submitted for backend
validation and separate immutable persistence before they can be selected for a Run; the
response SHALL identify acquisition versus analysis-configuration snapshot provenance.

#### Scenario: Static Run is submitted

- **WHEN** a client selects a runnable official algorithm, a valid historical montage,
  valid analysis outputs, and a valid range
- **THEN** WPF creates one Run containing the selected montage and output mapping
- **AND** WPF does not calculate any scientific value locally

#### Scenario: Montage changes before submission

- **WHEN** the user changes the historical montage from REF to AV before submitting
- **THEN** the request contains AV and the corresponding output channel names
- **AND** the request does not contain the prior REF output as the active analysis input

#### Scenario: Montage mapping is unavailable

- **WHEN** the requested montage cannot resolve one of the recorded source channels
- **THEN** WPF refuses submission with a structured mapping error
- **AND** no Run is created

