# Spec Delta

## Purpose

Provide one consistent, non-destructive projection of enabled EEG channels for acquisition and review displays while preserving the source channel identities used to read samples.

## ADDED Requirements

### Requirement: Project enabled EEG channels from the bound configuration
Desktop channel displays SHALL include only enabled EEG channels from the immutable configuration bound to the active acquisition or recording, ordered by configured display order while retaining source stream indices.

#### Scenario: A recording has disabled channels
- **WHEN** a recording's bound channel snapshot marks one EEG channel disabled
- **THEN** review waveform and channel choices omit that channel
- **AND** enabled channels still read samples from their original stream indices

#### Scenario: Display order differs from source order
- **WHEN** enabled channel display order differs from the stored channel table order
- **THEN** the display follows configured order
- **AND** each displayed channel reads values from its unchanged source stream index

### Requirement: Use one enabled-channel projection for impedance and live analysis
Impedance presentation and live analysis channel choices SHALL use the same enabled EEG selection semantics as other desktop channel displays. SDK readings for channels outside that selection SHALL not create visible rows.

#### Scenario: Impedance SDK reports a disabled channel
- **WHEN** the SDK returns a reading for a channel disabled in the bound physical configuration
- **THEN** the impedance view omits that reading
- **AND** enabled configured channels remain associated with their native identities

#### Scenario: Impedance SDK compacts physical input indexes
- **WHEN** the SDK returns the requested number of physical reference channels with stream-local indexes
- **THEN** the stream metadata maps those channels to requested physical identities in SDK stream order
- **AND** each value uses the matching configured electrode label and position

#### Scenario: Impedance SDK physical channel count differs from request
- **WHEN** the SDK returns fewer or more physical reference channels than requested
- **THEN** opening impedance fails with a channel identity error
- **AND** the client does not guess which configured electrode owns a stream value

#### Scenario: Impedance SDK returns no requested physical inputs
- **WHEN** the SDK stream has no physical reference input matching the request
- **THEN** opening impedance fails with a channel identity error

#### Scenario: Live analysis opens with a disabled input
- **WHEN** live analysis channel choices are built from an acquisition snapshot containing disabled EEG inputs
- **THEN** only enabled EEG choices are offered
- **AND** the acquisition stream and raw recording remain unchanged

### Requirement: Preserve source channels when no verified configuration applies
When no immutable configuration is available or channel identities cannot be verified, desktop presentation SHALL retain the source EEG channels in source order and SHALL not guess labels or selection state.

#### Scenario: Legacy recording has no channel snapshot
- **WHEN** review opens a recording without a bound channel configuration snapshot
- **THEN** it retains the recorded EEG channel list in source order
- **AND** it reports no configuration-based filtering as applied

### Requirement: Use the same projection for live waveform and analysis
Live waveform, live analysis, impedance, and review displays SHALL use the shared validated projection semantics. The projection SHALL preserve raw stream indexes and SHALL reject mismatched native identity, channel kind, or source label rather than silently relabeling a source channel.

#### Scenario: Live source metadata conflicts with its snapshot
- **WHEN** a live source channel has a native index present in the snapshot but its kind or label differs
- **THEN** the projection is reported invalid and source channels remain identifiable in source order
- **AND** the waveform does not silently display the configured electrode label for the mismatched source
