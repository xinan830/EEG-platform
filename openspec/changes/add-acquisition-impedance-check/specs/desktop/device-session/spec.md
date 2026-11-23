# Device session delta

## MODIFIED Requirements

### Requirement: Runtime transitions update the shared device snapshot

The device session SHALL translate an active EEG runtime transition into
`streaming`, active impedance measurement into `impedance-measuring`, mode
opening/closing into `switching-mode`, and a runtime fault into `faulted`,
retaining the last selected device identity for diagnosis. A later successful
discovery SHALL replace a faulted snapshot only after native ownership cleanup.

#### Scenario: A stream faults after device removal

- **WHEN** the acquisition runtime reports a fault while a selected device is present
- **THEN** the device session SHALL publish `faulted` with the runtime detail
- **AND** dependent pages SHALL observe that the device is not ready for a new setup action until successful discovery occurs.

#### Scenario: Impedance state is visible across pages

- **WHEN** the operator opens or closes impedance measurement
- **THEN** all device consumers observe the same switching and measurement state
- **AND** none reports the occupied device as idle or opens an independent stream

#### Scenario: Cleanup fails after a mode fault

- **WHEN** native release cannot be confirmed after an EEG or impedance failure
- **THEN** the device remains faulted and unavailable for new streams
- **AND** discovery cannot clear the ownership fault without successful cleanup or process restart

## ADDED Requirements

### Requirement: Hardware occupancy covers every measurement mode
Background availability checks, explicit discovery, driver reconfiguration and device selection SHALL NOT access or replace a device while EEG, impedance or mode cleanup owns it. A pending availability request SHALL recheck ownership under the shared lifecycle boundary before native discovery. Operation serialization alone SHALL NOT be treated as mode ownership.

#### Scenario: Availability timer or Windows notification during detection
- **WHEN** a refresh request arrives during impedance opening, measuring or closing
- **THEN** no native enumeration or adapter replacement occurs
- **AND** the selected device, channel configuration and measurement remain unchanged

#### Scenario: A queued refresh races mode opening
- **WHEN** a queued availability refresh resumes after a measurement mode has acquired device ownership
- **THEN** it observes that ownership before calling the SDK and does not enumerate

### Requirement: Mode changes preserve compatible acquisition setup
A change between idle, EEG and impedance for the same device facts SHALL preserve selected project, sampling rate, saved acquisition configuration and its physical source channel snapshot. Actual device capability changes SHALL require compatibility validation.

#### Scenario: End preview to detect impedance
- **WHEN** EEG preview ends to start impedance on the same physical device
- **THEN** saved acquisition choices remain selected
- **AND** detection uses physical source channels from the acquisition configuration
