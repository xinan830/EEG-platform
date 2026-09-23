# Montage and reference baseline

## Purpose

Define channel mapping, display montage, and analysis reference as explicit and separate concepts.

## Requirements

### Requirement: Preserve requested channel order

The system SHALL match requested channel labels case-insensitively and return matched source labels in exactly the requested order.

#### Scenario: Request a non-source order

- **WHEN** a client requests `Oz,Fz,Pz`
- **THEN** output channels are ordered `Oz,Fz,Pz` using the source file's original label spelling

### Requirement: Validate semantic channel mappings

The current mapping SHALL require distinct Fz, Pz, and Oz source channels and SHALL allow F3 and F4 only as a pair.

#### Scenario: Save an incomplete asymmetry pair

- **WHEN** only F3 or only F4 is provided
- **THEN** the system rejects the mapping with a validation error

### Requirement: Separate display montage from analysis reference

The system SHALL NOT infer the scientific analysis reference from the waveform display montage.

#### Scenario: Select an average display montage

- **WHEN** the user changes the viewer montage
- **THEN** the fixed `offline-spectral-v3` reference remains `original_recording_no_software_rereference`

### Requirement: Reject missing montage inputs

The system SHALL reject a montage or analysis request whose required source channels do not exist rather than substituting a positional channel.

#### Scenario: Request an unavailable named channel

- **WHEN** a requested scientific channel cannot be matched to a source label
- **THEN** the operation returns a validation failure and does not calculate from another channel

### Requirement: Validate reusable channel configurations before publication

The desktop SHALL validate a channel configuration before saving or applying
it. A valid profile SHALL contain the complete compatible EEG input set,
unique native input indexes, unique non-empty electrode labels, unique display
orders, and at least one displayed channel. Every displayed channel SHALL have
an electrode label.

#### Scenario: An invalid profile is applied

- **WHEN** a profile has duplicate labels, duplicate display orders, an unnamed displayed channel, or no displayed channel
- **THEN** application fails before the current applied snapshot changes
- **AND** the previously applied snapshot remains current.

### Requirement: Applied channel status identifies exact content

The desktop SHALL distinguish an exact applied channel snapshot from a saved
profile with the same identifier but different semantic content.

#### Scenario: An applied profile is edited and saved

- **WHEN** the catalogue profile keeps its identifier but its mapping, display selection, order, REF location, or GND location changes
- **THEN** the profile is marked as having unapplied changes
- **AND** it is not represented as the exact current snapshot until explicitly applied.

### Requirement: Montage profiles disclose source snapshot currency

A saved montage SHALL retain its immutable channel-configuration snapshot and
SHALL disclose whether that snapshot matches the current catalogue profile,
the source profile has changed, the source profile was removed, or the current
device is incompatible.

#### Scenario: A source channel profile changes

- **WHEN** a channel profile used to create a montage is edited without recreating the montage
- **THEN** the montage retains its original source snapshot
- **AND** the catalogue marks the montage source as modified rather than silently treating it as synchronized.

### Requirement: Referenced channel signal definitions are immutable

After any saved montage references a user channel profile, the desktop SHALL
reject in-place changes to its input mapping, electrode labels, display
selection, display order, hardware REF/GND locations, device compatibility, or
deletion. The profile name and description MAY change because they do not alter
signal identity.

#### Scenario: An operator edits a referenced channel profile

- **WHEN** one or more montage profiles reference the channel profile
- **THEN** its signal-bearing controls are read-only
- **AND** a signal-bearing change submitted through another path is rejected by the business rule
- **AND** name or description changes remain allowed.

#### Scenario: An operator deletes a referenced channel profile

- **WHEN** one or more montage profiles reference the channel profile
- **THEN** deletion is rejected with the number of references
- **AND** no montage or channel profile is removed.

### Requirement: Referenced channel profiles support version creation

The desktop SHALL allow an operator to create a new user profile from a locked
profile. The new profile SHALL have a new identity and editable signal fields;
the original profile and its montage references SHALL remain unchanged.

#### Scenario: Create a new version from a locked profile

- **WHEN** an operator chooses create new version
- **THEN** the draft starts with the locked profile's mapping and metadata
- **AND** saving creates a new profile identifier
- **AND** existing montages continue to reference the original profile.
