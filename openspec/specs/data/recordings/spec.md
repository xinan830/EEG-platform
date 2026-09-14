# Recording data baseline

## Purpose

Define the current import, storage, metadata, channel-order, and source-file behavior that future traceability work must preserve.

## Requirements

### Requirement: Import supported EEG files

The system SHALL accept non-empty EDF and BDF files, store each file under an internal recording identifier, and reject unsupported or unreadable files without retaining a partial recording.

#### Scenario: Import a readable recording

- **WHEN** a user imports a non-empty readable EDF or BDF file
- **THEN** the system stores it under a generated internal name and returns the recording identifier, original base filename, extension, sampling frequency, duration, and file channel labels

#### Scenario: Reject an unreadable recording

- **WHEN** metadata cannot be read from the uploaded file
- **THEN** the system returns a validation failure and removes both the partially stored file and metadata row

### Requirement: Preserve source channel order

The system SHALL preserve the order of channel labels reported by the source file unless an explicit channel selection or montage defines another output order.

#### Scenario: List imported channels

- **WHEN** recording metadata is returned
- **THEN** channel labels appear in source-file order

### Requirement: Keep source files immutable

The system SHALL treat imported EEG files as read-only inputs. Preview, playback, filtering, montage, and analysis SHALL create derived in-memory or persisted values without modifying the source bytes.

#### Scenario: Analyze a recording

- **WHEN** any current viewer or analysis operation runs
- **THEN** it reads the stored source file and does not overwrite that file

### Requirement: Expose current recording resources

The system SHALL retain the current recording import, list, detail, preview, window, montage, mapping, spectrum, spectrogram, playback, and legacy analysis routes while incremental migration is in progress.

#### Scenario: Upgrade internal storage

- **WHEN** a later change adds metadata or derived resources
- **THEN** existing recording routes remain callable with their existing request forms unless a separately approved compatibility change says otherwise
