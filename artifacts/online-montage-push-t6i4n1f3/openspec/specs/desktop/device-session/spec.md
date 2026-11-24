# desktop/device-session Specification

## Purpose
This capability defines the single desktop owner for hardware discovery, device facts, compatibility, and runtime session transitions.
## Requirements
### Requirement: Desktop hardware state has one session owner

The desktop SHALL expose discovered devices, the selected physical device, its
driver ID, and its capability fingerprint through one `DeviceSession` owner.
Desktop pages SHALL read session state and SHALL NOT call a vendor SDK or keep
an independent selected-device value.

For hardware metadata, a driver MAY provide a canonical model and a separate
device-instance identifier while preserving the vendor serial value. Pages
SHALL display those declared fields and SHALL NOT parse vendor display names.

#### Scenario: Discovery finds one device

- **WHEN** a configured device driver successfully reports exactly one device
- **THEN** the device session SHALL select that returned descriptor
- **AND** publish a `connected` snapshot with the driver ID and capability fingerprint derived from the returned physical channel capabilities
- **AND** channel configuration setup SHALL read that same selected descriptor.

#### Scenario: Discovery finds no device

- **WHEN** a configured device driver reports no devices
- **THEN** the device session SHALL publish `disconnected`
- **AND** clear the selected device and its capability fingerprint
- **AND** pages SHALL not offer the stale device as an available source.

#### Scenario: An idle device is removed after discovery

- **WHEN** a configured, non-streaming device session performs its periodic availability refresh
- **AND** the driver no longer reports the previously selected device
- **THEN** the device session SHALL publish `disconnected`
- **AND** clear the stale selected descriptor and capability fingerprint without requiring a page refresh.

#### Scenario: An unchanged background availability refresh

- **WHEN** an idle device session refresh discovers the same devices with the same reported capabilities
- **THEN** the session SHALL retain its existing device descriptor objects and snapshot
- **AND** dependent setup controls SHALL retain their current selections without being reset.

#### Scenario: A Windows device-change notification is received

- **WHEN** the desktop receives a Windows device-change notification while not streaming
- **THEN** it SHALL delay a bounded debounce interval before requesting SDK discovery
- **AND** the notification itself SHALL NOT be treated as proof that an EEG device is connected or usable.

#### Scenario: An idle session has no Windows device-change notification

- **WHEN** an idle configured session receives no device-change notification
- **THEN** it SHALL perform a low-frequency SDK availability confirmation as a fallback
- **AND** it SHALL NOT enumerate devices while an EEG stream is active.

#### Scenario: Recording begins after a stale connected display

- **WHEN** an operator starts a recording from a previously connected device display
- **THEN** the session SHALL reconfirm the selected device through the SDK before opening the stream
- **AND** a successfully opened stream SHALL remain the final proof that the device is usable.

### Requirement: Device facts remain distinct from setup choices

The device session SHALL retain only device-reported or lifecycle facts. A
selected sampling rate, project, channel profile, montage, filter preference,
and recording destination SHALL remain acquisition setup state until a stream
is opened.

#### Scenario: The user selects a sampling rate

- **WHEN** an operator chooses one device-supported sampling rate before a stream opens
- **THEN** that choice SHALL NOT be represented as an actual stream sampling rate in the device session
- **AND** the session SHALL continue to expose the device-supported rate list.

### Requirement: Channel configuration compatibility excludes device instances

Reusable channel configurations SHALL bind to a deterministic signature of
driver ID and all reported physical channel capabilities, including native
input index, role, and unit. A model MAY be retained for display and filtering,
but a serial number or device instance SHALL NOT participate in compatibility.

#### Scenario: The same model is replaced by another physical instance

- **WHEN** a device with the same driver and identical physical capabilities is connected under a different serial number or instance identifier
- **THEN** compatible channel configurations SHALL remain applicable
- **AND** the newly connected instance SHALL remain independently visible for acquisition traceability.

#### Scenario: Two devices have a superficially similar model but incompatible inputs

- **WHEN** a connected device has a different driver, input role, index, or unit from a saved channel configuration
- **THEN** the configuration SHALL be marked as incompatible
- **AND** the shared model label alone SHALL NOT allow it to be applied.

### Requirement: Hardware REF and GND locations are operator-recorded setup facts

The desktop SHALL retain fixed hardware roles `REF` and `GND` separately from
the EEG input mapping. A reusable channel configuration MAY retain the
operator-recorded scalp location for each role, but the SDK or a BDF channel
list SHALL NOT be treated as the source of those locations. When a configured
stream opens, the recorded locations and fixed roles SHALL be included in its
immutable hardware configuration snapshot; missing locations SHALL be recorded
as unavailable rather than inferred.

#### Scenario: An operator saves REF and GND locations with a channel configuration

- **WHEN** an operator enters the actual scalp locations of the fixed REF and GND leads
- **THEN** the values SHALL be saved with that reusable channel configuration
- **AND** the values SHALL remain separate from the 0–27 EEG input mapping.

#### Scenario: A recording begins with no locations recorded

- **WHEN** a channel configuration has no recorded REF or GND scalp location
- **THEN** the recording snapshot SHALL retain the fixed hardware roles
- **AND** the corresponding location values SHALL be recorded as unavailable rather than guessed.

### Requirement: Runtime transitions update the shared device snapshot

The device session SHALL translate an active EEG runtime transition into
`streaming` and a runtime fault into `faulted`, retaining the last selected
device identity for diagnosis. A later successful discovery SHALL replace a
faulted snapshot.

#### Scenario: A stream faults after device removal

- **WHEN** the acquisition runtime reports a fault while a selected device is present
- **THEN** the device session SHALL publish `faulted` with the runtime detail
- **AND** dependent pages SHALL observe that the device is not ready for a new setup action until successful discovery occurs.

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
