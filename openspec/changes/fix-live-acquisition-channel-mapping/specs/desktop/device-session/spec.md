## ADDED Requirements

### Requirement: Acquisition status changes preserve selected channel setup

The acquisition workspace SHALL distinguish runtime status notifications from
changes to the selected physical device facts. Equivalent device descriptors
SHALL NOT reload channel defaults, legacy label mappings or setup choices.
The mapping selected from the montage's saved channel snapshot SHALL remain
effective for live display through preview, recording, pause, resume and stop.
EEG native input identity, stream order, units, time and raw bytes SHALL remain
unchanged by this display correction.

#### Scenario: A custom electrode label survives stream setup

- **GIVEN** a selected montage binds CH3 to F88 and CH5 to F3
- **WHEN** the same device publishes preparation or streaming status changes
- **THEN** live display SHALL retain F88-REF from CH3 and F3-REF from CH5
- **AND** neither output SHALL be omitted or resolved using an older label map.

#### Scenario: An existing electrode name is moved to another native input

- **GIVEN** a valid selected snapshot moves F7 from CH2 to CH3 without duplicate labels
- **WHEN** preview, recording, pause or resume changes the runtime state
- **THEN** F7-REF SHALL continue to read CH3 rather than the legacy CH2 input.

#### Scenario: The actual device capabilities change

- **WHEN** discovery publishes a selected device with changed capabilities or identity
- **THEN** the workspace SHALL refresh device-backed setup
- **AND** it SHALL NOT retain a channel map as valid for incompatible inputs.

#### Scenario: Discovery confirms disconnection

- **WHEN** the selected device becomes unavailable and the session clears it
- **THEN** acquisition setup SHALL clear stale device-backed channel rows
- **AND** later successful discovery SHALL initialize the newly available device.
