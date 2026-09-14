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
