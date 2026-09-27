## MODIFIED Requirements

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
