## Purpose

Provide portable raw EEG exports from registered recordings with explicit scientific coordinates, bounded resource use and verifiable provenance.

## ADDED Requirements

### Requirement: Export registered raw recordings
The backend SHALL accept POST `/api/recordings/{recording_id}/signal-export` with `format` equal to `edf` or `bdf` and return an application/zip package containing the complete raw signal and versioned metadata sidecar. The backend SHALL reject derived montage registrations, unsupported formats, missing sources, unsupported units, source gaps and non-finite samples with stable error codes and request identity. It SHALL NOT accept an arbitrary output path or modify source data. Events SHALL use recorded recording-relative samples and the recorded UTC start. Finite suspicious extremes SHALL be preserved and described rather than silently removed or replaced.

#### Scenario: Complete export
- **WHEN** a valid raw recording is exported
- **THEN** every stored Reference/Bipolar signal and source sample appears in source order, with explicit V-to-uV conversion and the source rate
- **AND** the package records source identity, raw processing, channel mapping, available reference/montage provenance and per-channel quantization
- **AND** available resolved events are exported at their sample-derived times.

#### Scenario: Invalid source
- **WHEN** the source has a missing interval, non-finite data, unsupported units or has changed since registration
- **THEN** the request fails with a structured reason and no successful package.

### Requirement: Bound export resource ownership
Export SHALL use bounded source blocks and SHALL NOT load an entire long recording into RAM. Only one export SHALL be admitted per backend process at a time; additional requests SHALL receive a busy error. Cancellation, disconnection and failures SHALL release readers, writer handles, temporary artifacts and the export slot.

#### Scenario: Overlapping exports
- **WHEN** another export is already admitted
- **THEN** the new request receives HTTP 409 with code `RECORDING_EXPORT_BUSY`.

#### Scenario: Cancelled delivery
- **WHEN** a client cancels or disconnects
- **THEN** backend work stops at a bounded checkpoint and temporary files are removed.
