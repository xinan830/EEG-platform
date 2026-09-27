# desktop/event-annotations Specification

## Purpose
TBD - created by archiving change add-event-annotation-system. Update Purpose after archive.
## Requirements
### Requirement: Manage reusable event definitions

The desktop SHALL provide a settings-managed event definition catalog. Each
definition SHALL have a stable unique system-generated `Code` that users cannot
edit after creation, a user-facing name, a display
color, an enabled state, an optional shortcut, a source classification, and a
version. Definition validation SHALL reject blank names, duplicate Codes,
invalid colors, descriptions longer than 200 characters, and conflicting
shortcuts. The editor SHALL retain its draft when validation or persistence
fails.

For a new definition, the editor SHALL generate the Code before displaying the
form and start with an empty name. The Code SHALL be read-only, and the editor
SHALL not offer save until the name contains non-whitespace text. Existing
definition Codes SHALL remain unchanged. Shortcut entry
SHALL capture physical key input rather than accepting arbitrary typed text.

#### Scenario: Create a valid user event

- **WHEN** a user saves an event with a system-generated unique Code, non-empty name, valid
  color, description of at most 200 characters, and an available shortcut
- **THEN** the definition SHALL be persisted
- **AND** it SHALL become available to acquisition and review event controls
  when enabled
- **AND** its version SHALL be recorded.

#### Scenario: Reject a duplicate Code

- **WHEN** a user saves an event whose Code matches another active or
  historical definition
- **THEN** saving SHALL fail with a structured validation reason
- **AND** no existing definition SHALL be overwritten.

#### Scenario: Keep an existing event Code immutable

- **WHEN** a definition update attempts to change its Code
- **THEN** saving SHALL fail with a structured validation reason
- **AND** existing definitions and historical event snapshots SHALL retain their Codes.

#### Scenario: Reject an overlong description

- **WHEN** a definition with more than 200 description characters is saved
- **THEN** saving SHALL fail with a structured validation reason
- **AND** the editor SHALL retain the draft for correction.

#### Scenario: Disable an event used by history

- **WHEN** a user disables an event definition referenced by a RecordingEvent
- **THEN** it SHALL disappear from new acquisition event controls
- **AND** historical RecordingEvent values SHALL remain readable and displayable.

#### Scenario: Show historical references in the definition list

- **WHEN** the event-definition list is opened or refreshed
- **THEN** each definition SHALL show whether any persisted RecordingEvent references its Id
- **AND** an incomplete or failed reference scan SHALL be shown as unconfirmed, not as unreferenced
- **AND** a definition with a confirmed reference SHALL not offer physical deletion.

### Requirement: Persist recording events separately from raw EEG

The desktop SHALL persist RecordingEvent metadata separately from immutable raw EEG chunks. A RecordingEvent SHALL identify its Recording, definition, source, sample-based start, sample-based duration, and definition display snapshot.

#### Scenario: Save a point event during acquisition

- **WHEN** an enabled event is triggered by a button or registered shortcut during an active Recording
- **THEN** the desktop SHALL capture the current acquisition sample coordinate
- **AND** persist an event with `DurationSamples = 0`
- **AND** leave raw EEG values, triggers, counters, and recording state unchanged.

#### Scenario: Save an interval event

- **WHEN** a user starts and later ends an enabled interval event
- **THEN** the desktop SHALL persist its start sample and positive duration in the Recording sample coordinate
- **AND** its displayed duration SHALL be derived from the Recording sampling rate.

#### Scenario: Event persistence fails

- **WHEN** event metadata cannot be written
- **THEN** the desktop SHALL report the structured failure
- **AND** acquisition raw persistence and device reading SHALL continue
- **AND** the UI SHALL not claim that the event was saved.

### Requirement: Preserve scientific event time semantics

The desktop SHALL use Recording sample coordinates and the manifest sampling rate as the event time axis. PC wall-clock receive time SHALL NOT replace sample-based time for waveform alignment. Events referring to an unavailable or ambiguous sample coordinate SHALL be rejected or marked unavailable with a reason.

#### Scenario: Recording begins at a non-zero sample counter

- **WHEN** a device Recording starts with a non-zero first sample counter
- **THEN** an event at the current counter SHALL resolve to the corresponding Recording-relative sample coordinate
- **AND** review SHALL display the same event at the same waveform position.

#### Scenario: A recording has a sample gap

- **WHEN** an event falls inside a recorded sample-counter gap
- **THEN** the desktop SHALL preserve the event's original counter metadata
- **AND** it SHALL mark the display coordinate as unavailable rather than inserting EEG samples or moving the event silently.

#### Scenario: A device reconnect resets or discontinues the sample counter

- **WHEN** acquisition detects a device reconnect, sample-counter reset, or discontinuity
- **THEN** the Recording SHALL preserve a new counter segment/discontinuity marker
- **AND** event coordinates SHALL NOT be silently continued across the boundary
- **AND** an event in an unavailable segment SHALL be rejected or marked unavailable with a structured reason.

#### Scenario: An event is queried with combined filters

- **WHEN** the event service receives any combination of RecordingId, sample range, definition/code, and source filters
- **THEN** it SHALL return only events matching every supplied filter
- **AND** omitted filters SHALL not restrict the result.

### Requirement: Coordinate shortcut ownership

The desktop SHALL register shortcuts through one registry with explicit
`Global`, `Acquisition`, or `Review` scope. A Global shortcut SHALL conflict
with the same key in either workspace; Acquisition and Review MAY reuse a key.
Event shortcuts SHALL NOT replace an existing command registration without
an explicit conflict resolution. Modifier-only and auto-repeated key-down
input SHALL NOT create recording events.

#### Scenario: Event shortcut conflicts with playback

- **WHEN** a user assigns a shortcut already registered by playback or
  recording control in the same or overlapping scope
- **THEN** the event definition save SHALL be rejected
- **AND** the conflict response SHALL identify the existing command.

#### Scenario: Disabled event releases its shortcut

- **WHEN** a user disables an event definition
- **THEN** its shortcut SHALL no longer trigger event creation
- **AND** the shortcut SHALL become available for a later compatible registration.

#### Scenario: User holds an event shortcut

- **WHEN** a key produces repeated key-down messages without being released
- **THEN** only the initial key-down MAY create a recording event.

### Requirement: Share event controls between acquisition and review

The acquisition and review workspaces SHALL consume the same enabled event definition catalog, event source model, and event persistence service. They SHALL NOT maintain separate event type lists or color mappings.

#### Scenario: Acquisition event appears in review

- **WHEN** an acquisition creates a RecordingEvent successfully
- **THEN** opening that Recording in review SHALL show the event at its recorded sample position
- **AND** clicking the event SHALL seek the review cursor without modifying raw EEG.

#### Scenario: Review adds an annotation

- **WHEN** a user creates an allowed manual event in review
- **THEN** the event SHALL be persisted against the same Recording
- **AND** returning to the recording later SHALL restore it with its source and definition snapshot.

### Requirement: Protect historical event provenance

The desktop SHALL distinguish system, device-trigger, imported, algorithm, and manual event sources. System and externally sourced events SHALL be read-only unless a separately approved transformation creates a new manual annotation. User event definitions referenced by history SHALL be soft-deleted or disabled rather than physically removed.

#### Scenario: Definition display properties change

- **WHEN** a user changes the name or color of an existing event definition
- **THEN** existing RecordingEvent snapshots SHALL retain their historical display values
- **AND** new events MAY use the new definition version.

#### Scenario: A referenced definition is disabled or deletion is restricted

- **WHEN** a referenced event definition is disabled or cannot be physically deleted
- **THEN** existing RecordingEvent records SHALL remain loadable and displayable from their stored snapshots
- **AND** no historical event SHALL be removed as a side effect.

