# Acquisition impedance

## Purpose

Provide operator-controlled measurement of physical EEG electrode contact impedance through the connected amplifier, with explicit units, channel identity, measurement status and safe transitions between impedance checking and EEG recording.

## ADDED Requirements

### Requirement: Impedance uses a separate hardware measurement contract
The desktop SHALL expose impedance only through a supported device capability. Readings SHALL retain device identity, detection-session identity, device-reported channel role/index/order, a finite nonnegative resistance in ohms when valid, host receipt time and quality status. Host receipt time SHALL NOT be presented as the hardware measurement timestamp or EEG sample time.

#### Scenario: Read a valid impedance state
- **WHEN** the amplifier returns its latest impedance state
- **THEN** the desktop retains its returned channel identities and order without requiring an EEG sample counter or EEG sampling rate
- **AND** the UI displays resistance divided by 1000 with the explicit unit kΩ
- **AND** repeated values are not treated as proof of a stalled device or a new hardware measurement

#### Scenario: Unsupported device
- **WHEN** the selected driver or amplifier does not support impedance measurement
- **THEN** the entry is unavailable with an explicit reason
- **AND** existing EEG acquisition remains available without fabricated impedance values

### Requirement: EEG and impedance modes are mutually exclusive
The desktop SHALL serialize impedance, EEG opening, stopping, reconfiguration and cleanup through the existing device lifecycle. It SHALL complete closure of one hardware stream before opening the other, and reject duplicate or conflicting actions.

#### Scenario: Start from an idle connected device
- **WHEN** the operator starts impedance detection with a compatible physical channel configuration
- **THEN** a single impedance session opens and device state visibly reports impedance measurement
- **AND** EEG preview and recording cannot open while it owns the device

#### Scenario: Switch from preview
- **WHEN** the operator selects impedance detection during EEG preview
- **THEN** the current preview session and its pending display/analysis work end before impedance opens
- **AND** the UI explains that live EEG is suspended during measurement

### Requirement: Recording is finalized before impedance detection
An impedance request during recording or recording pause SHALL require explicit confirmation to end the current recording. The desktop SHALL successfully drain accepted recording data, finalize storage and close EEG before opening impedance. Cancellation or finalization failure SHALL NOT open impedance or discard accepted samples.

#### Scenario: Operator cancels while recording
- **WHEN** an operator cancels the request to end a recording for impedance detection
- **THEN** the current recording continues unchanged and no impedance stream opens

#### Scenario: Save succeeds or fails
- **WHEN** an operator confirms ending an active or paused recording
- **THEN** impedance opens only after successful recording finalization and EEG closure
- **AND** a storage or closure failure is shown explicitly, retains recoverable recording artifacts, and blocks detection
- **AND** any later recording creates a separate recording identity and file

### Requirement: Physical electrode identity comes from acquisition configuration
The impedance view SHALL map SDK native channel role/index to the selected acquisition configuration's physical channel snapshot, never to software-derived display or analysis channels. It SHALL use persisted electrode coordinates where available and preserve unmapped readings in a labelled list. REF and GND SHALL remain distinct physical roles with measured values only when returned by the SDK.

#### Scenario: Display or analysis channels differ from physical electrodes
- **WHEN** the displayed waveform or analysis uses derived channels and impedance detection starts
- **THEN** detection shows the mapped physical electrodes and their SDK identities
- **AND** it does not change acquisition configuration, recording metadata or scientific formulas

#### Scenario: Position or hardware landmark measurement is missing
- **WHEN** an electrode has no configured position or a REF/GND measurement is unavailable
- **THEN** the UI does not invent its position or resistance
- **AND** unpositioned measurements remain available by native identity in the list, while missing measurements show an explicit unavailable state

### Requirement: Impedance presentation uses explicit operator thresholds
The tool window SHALL offer an electrode map, selectable electrode details, a resistance-sorted list and a visible kΩ legend. For valid operator thresholds 0 < T1 < T2, values ≤ T1 SHALL be green, values > T1 and ≤ T2 yellow, and values > T2 red. Values and state text SHALL accompany colors. Thresholds SHALL NOT be labelled as manufacturer or medical acceptance standards without evidence.

#### Scenario: Thresholds are unset or invalid
- **WHEN** no valid threshold pair is configured
- **THEN** measurement remains available with numeric values and a neutral unclassified appearance
- **AND** the UI requests valid kΩ thresholds without silently choosing clinical limits

#### Scenario: Select and inspect one electrode
- **WHEN** the operator selects an electrode on the map or in the list
- **THEN** both views identify the same physical electrode and display its current resistance and status
- **AND** this selection does not start a second stream or resume EEG

#### Scenario: Invalid or unavailable reading
- **WHEN** a reading is absent, non-finite, negative, or explicitly invalid according to the verified SDK contract
- **THEN** it is shown as unavailable with a reason instead of 0 kΩ or a green state
- **AND** transport freshness is distinguished from whether the last numeric resistance changed

### Requirement: Detection cleanup and return to EEG are explicit
Closing the tool window, stopping detection, leaving the acquisition workspace or exiting the application SHALL cancel and release impedance ownership. Returning to preview SHALL close impedance first and create a new EEG session; recording SHALL require a separate user action. Release failure SHALL block new streams until recovery.

#### Scenario: Return to preview after checking electrodes
- **WHEN** the operator selects end detection and enter preview
- **THEN** impedance closes before a new EEG preview opens using validated current acquisition settings
- **AND** old EEG/impedance callbacks cannot populate the new session
- **AND** pre-detection samples and analysis windows are not joined to post-detection samples

#### Scenario: Close without requesting preview
- **WHEN** the operator closes the tool window or leaves the acquisition workspace
- **THEN** detection ends and the device is released without automatically starting preview or recording

#### Scenario: Device failure or disconnection
- **WHEN** impedance opening or reading fails or the amplifier disconnects
- **THEN** the UI reports the detection fault, visibly invalidates old live readings and attempts ordered cleanup
- **AND** a successfully cleaned device requires reconfirmation before a new stream opens
- **AND** uncertain native ownership is not reported as idle

### Requirement: Impedance remains outside EEG recording and analysis
The desktop SHALL keep impedance snapshots out of raw EEG writers, display filtering, algorithm transport, scientific time axes and offline review. Detection logs SHALL identify device, session, transition and structured failures without dumping continuous physiological measurements.

#### Scenario: Detection runs or mode changes
- **WHEN** impedance values arrive or the operator changes detection mode
- **THEN** no impedance values are submitted as EEG batches, persisted as EEG samples or sent to live algorithms
- **AND** existing backend protocols and saved recording formats remain unchanged
