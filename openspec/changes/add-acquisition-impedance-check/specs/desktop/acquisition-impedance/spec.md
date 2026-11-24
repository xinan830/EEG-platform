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
- **AND** the visible waveform and sweep erase band remain frozen until preview resumes
- **AND** resumed preview follows the frozen display history on a display-only timeline with an explicit segment break
- **AND** samples retained for this display are never reused for recording, filtering or analysis

#### Scenario: Preserve preview display across impedance
- **WHEN** the operator closes impedance after opening it from preview
- **THEN** the last visible EEG trace remains on screen while impedance owns the device
- **AND** new EEG samples continue from the frozen trace position and begin a new disconnected segment
- **AND** the impedance interval is not represented as EEG samples or as a continuous waveform
- **AND** a different sampling rate or physical stream layout starts a new display instead of mixing incompatible channel data

### Requirement: Recording blocks impedance detection
The impedance entry SHALL be disabled while recording or recording is paused. The workspace and acquisition runtime SHALL reject impedance requests in either state, without stopping EEG or finalizing the active recording.

#### Scenario: Impedance entry during recording
- **WHEN** the operator starts or pauses a recording
- **THEN** the impedance entry is disabled
- **AND** direct or stale UI requests are rejected while EEG and the active recording remain unchanged

#### Scenario: Detection after recording finishes
- **WHEN** the recording is fully stopped and the device is available
- **THEN** the operator may open impedance detection from the idle state
- **AND** returning to preview creates a new EEG session and a later recording receives a separate identity

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
The tool window SHALL show a selectable physical-electrode map with kΩ values and a continuous 0–200 kΩ color band that also acts as its upper-limit slider. The slider SHALL default to 20 kΩ; its three editable colors apply at 0, half the selected limit and the selected limit, with higher values saturated at the high color. Missing or stale readings remain gray. Colors SHALL NOT imply medical thresholds.

#### Scenario: Share named circle presentation
- **WHEN** channel positions or impedance electrodes are displayed
- **THEN** both use the shared circle with a centered electrode name, with impedance readings above the circle
- **AND** configured positions and manual position editing remain unchanged
- **AND** existing algorithm topomap black dots, coordinates and rendering remain unchanged

#### Scenario: Refine electrode position window layout
- **WHEN** the electrode position window opens
- **THEN** it is maximized, preserves its explanatory text and enlarges its editable head map with matching hit and drag geometry
- **AND** the default REF diagram marker is separated from Pz in both channel and impedance views, including profiles with the former default coordinate
- **AND** saved profiles are not rewritten, other custom positions and standard EEG coordinates remain unchanged

#### Scenario: Select a third electrode position view
- **WHEN** the operator switches among 2D, 3D and topographic view
- **THEN** exactly one view is visible; topographic view uses the same configured positions and shared black dot rendering without generated measurements
- **AND** only 2D edits positions, while switching preserves the chosen configuration and existing instruction text

#### Scenario: Pan the three dimensional head
- **WHEN** the operator holds Shift and drags in the 3D view
- **THEN** the visible head moves in the pointer's horizontal and vertical screen direction for any valid camera orientation
- **AND** panning does not alter electrode coordinates, rotation or scientific results

#### Scenario: Thresholds are unset or invalid
- **WHEN** a scale color is invalid
- **THEN** measurement remains available with numeric values and a neutral unclassified appearance
- **AND** unavailable readings remain gray and the UI does not silently apply an invalid color

#### Scenario: Edit continuous scale colors and upper limit
- **WHEN** the operator edits any of the three colors or drags the thumb on the color band
- **THEN** the legend, electrode fills and list indicators use the same updated mapping
- **AND** the band spans 0–200 kΩ, the selected upper limit can be 0–200 kΩ and defaults to 20 kΩ, and the middle anchor follows half of the upper limit
- **AND** missing or stale readings remain unavailable gray regardless of the configured gradient
- **AND** the map and gradient bar use the initial green, yellow and red colors without implying clinical limits

#### Scenario: Inspect impedance color range
- **WHEN** the impedance map is displayed
- **THEN** the electrode map is enlarged and the integrated gradient slider clearly labels 0, the selected upper limit and 200 kΩ
- **AND** changing a color updates the bar and electrode colors through the same scale

#### Scenario: Select and inspect one electrode
- **WHEN** the operator selects an electrode on the map or in the list
- **THEN** both views identify the same physical electrode and display its current resistance and status
- **AND** this selection does not start a second stream or resume EEG

#### Scenario: Invalid or unavailable reading
- **WHEN** a reading is absent, non-finite, negative, or explicitly invalid according to the verified SDK contract
- **THEN** it is shown as unavailable with a reason instead of 0 kΩ or a green state
- **AND** transport freshness is distinguished from whether the last numeric resistance changed

### Requirement: Detection cleanup releases hardware ownership
Closing the tool window, stopping detection, leaving the acquisition workspace or exiting the application SHALL cancel and release impedance ownership. Release failure SHALL block new streams until recovery.

#### Scenario: Cleanup after leaving acquisition
- **WHEN** the operator leaves the acquisition workspace or exits the application
- **THEN** detection ends and releases the device without automatically starting preview or recording

### Requirement: Preview resumes as a fresh session after impedance
Closing an impedance window opened from preview SHALL release impedance first and start a new EEG preview session. Closing after an idle start SHALL remain idle. Recording SHALL require a separate user action.

#### Scenario: Return to preview after checking electrodes
- **WHEN** the operator selects end detection and enter preview
- **THEN** impedance closes before a new EEG preview opens using validated current acquisition settings
- **AND** old EEG/impedance callbacks cannot populate the new session
- **AND** pre-detection samples and analysis windows are not joined to post-detection samples

#### Scenario: Close detection after preview
- **WHEN** the operator closes the tool window that was opened from EEG preview
- **THEN** detection ends and releases the device before a new EEG preview starts
- **AND** the new preview contains no pre-detection samples and does not start recording

#### Scenario: Close detection from idle
- **WHEN** the operator closes a tool window opened from idle
- **THEN** detection ends and releases the device without automatically starting preview or recording

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
