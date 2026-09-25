# analysis/legacy-compatibility Specification

## Purpose
TBD - created by archiving change remove-legacy-user-algorithm-compatibility. Update Purpose after archive.
## Requirements
### Requirement: Raw recordings are preserved during cleanup

The cleanup migration SHALL remove only retired user-algorithm data and its
derived artifacts. It SHALL preserve the recordings table and raw recording
files.

#### Scenario: Cleanup runs on an existing database

- **WHEN** migration 010 is applied
- **THEN** no `definition_metric`, `definition_preview`, or local-user
  Definition rows remain
- **AND** official Definition rows remain
- **AND** raw recording rows and files remain

