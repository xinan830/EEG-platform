## ADDED Requirements

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
