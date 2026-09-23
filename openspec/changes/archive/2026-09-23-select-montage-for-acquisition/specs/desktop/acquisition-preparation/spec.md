## ADDED Requirements

### Requirement: Acquisition uses the selected montage as its configuration source

The desktop SHALL require a valid montage selected in the acquisition
preparation controls before opening an EEG stream. The montage SHALL carry the
immutable channel-configuration snapshot used for that acquisition session.

#### Scenario: A user starts acquisition

- **WHEN** the user selects a valid montage and sampling rate and starts
  acquisition
- **THEN** the desktop SHALL validate the current device against the montage's
  channel snapshot
- **AND** it SHALL open the stream with that snapshot's channel labels and
  hardware REF/GND locations
- **AND** it SHALL persist both channel and montage snapshots in stream
  metadata.

#### Scenario: No montage is selected

- **WHEN** the user has not selected a montage
- **THEN** starting acquisition SHALL be unavailable or fail with a readable
  preparation error
- **AND** no stream SHALL be opened.

### Requirement: Channel configuration does not have a global apply state

The desktop SHALL not require a user to apply a channel configuration before a
montage can reference it. Device discovery SHALL not silently restore a global
active channel snapshot as the acquisition source.

#### Scenario: A valid channel profile is saved

- **WHEN** a valid channel profile is saved for the current device
- **THEN** it SHALL be available to montage configuration
- **AND** no global acquisition mapping SHALL change until an acquisition
  preparation session selects a montage.

### Requirement: Montage rendering preserves raw samples

The desktop MAY derive display traces from the selected montage, but SHALL
leave the retained and persisted raw EEG batches unchanged.

#### Scenario: A montage uses a software reference

- **WHEN** the selected montage is average, specified-pair, or channel
  referenced
- **THEN** the display trace SHALL be computed from the retained raw channel
  values
- **AND** the raw recording SHALL retain the original device channel samples.
