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

### Requirement: Identify immutable source bytes

Each recording SHALL persist the SHA-256 and byte length of its stored source file. Existing rows SHALL be backfilled when the source file is available and SHALL remain readable with an explicit unavailable identity when it is not.

#### Scenario: Import a new recording

- **WHEN** a readable EDF or BDF is imported
- **THEN** its recorded SHA-256 matches the exact stored bytes and its byte length matches the file size

#### Scenario: Upgrade a missing legacy source

- **WHEN** an existing metadata row references a source file that is not present during migration
- **THEN** migration completes without deleting the row and its source identity remains explicitly unavailable

### Requirement: Persist channel import metadata

The recording SHALL persist ordered raw labels, ordered canonical labels, channel types, channel units, and an import-contract version. Raw labels SHALL remain lossless and canonicalization SHALL NOT silently merge two distinct source channels.

#### Scenario: Normalize imported labels

- **WHEN** source labels contain case or surrounding whitespace differences
- **THEN** raw labels preserve source text, canonical labels use deterministic normalization, and array positions remain aligned across labels, types, and units

### Requirement: Apply versioned non-destructive migrations

The database SHALL maintain a single schema version and apply ordered migrations transactionally and idempotently without deleting existing recording, analysis, event, audit, or report data.

#### Scenario: Start with an existing database

- **WHEN** the application opens a supported older schema
- **THEN** each pending migration runs once, existing rows remain readable, and schema version advances only after successful commit

#### Scenario: Repeat startup

- **WHEN** the application starts again after all migrations completed
- **THEN** migration makes no duplicate tables, columns, or data changes

#### Scenario: Migration step fails

- **WHEN** a migration statement or backfill fails before commit
- **THEN** that migration is rolled back and the stored schema version is not advanced
