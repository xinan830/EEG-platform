# desktop/review-signal-export Specification

## Purpose
Define export behavior for raw signal intervals in the desktop recording review workspace.

## Requirements
## ADDED Requirements

### Requirement: Export the complete recording as raw EDF or BDF
The review workspace SHALL offer EDF and BDF export for the complete recording sample range `[first_sample_counter, last_sample_counter + 1)`. The export SHALL use original stored `Reference` and `Bipolar` channels in source order and SHALL NOT apply display filters, sensitivity, or viewing montage. The device sample-counter channel and non-EEG auxiliary channels SHALL NOT be exported as EEG signals. Source labels that fit the EDF/BDF label field (1 to 16 printable ASCII characters) SHALL be preserved. Other source labels SHALL receive stable unique short aliases, and a companion `<export-file>.labels.json` SHALL preserve all source-to-export channel mappings, channel kinds, units, recording identity, sampling rate, recording start, and available hardware/channel configuration metadata. Hardware serial and device instance identifiers SHALL NOT be copied to the sidecar. Each EDF/BDF data record SHALL be 1 millisecond through 60 seconds; longer recordings SHALL be divided into complete records without dropping or padding samples. The exported EDF/BDF sampling frequency SHALL agree with the source rate within the format's 10-microsecond data-record-duration precision.

#### Scenario: Export a continuous recording
- **WHEN** the user exports a recording whose source samples are contiguous
- **THEN** the selected EDF or BDF file contains every source sample in the complete recording range
- **AND** the file preserves source sampling frequency, channel order, and labels
- **AND** source values in volts are explicitly converted to physical microvolts
- **AND** the exported start time equals the recording start time plus the first exported recording-relative sample offset
- **AND** the source recording remains unchanged.

#### Scenario: Export events in the interval
- **WHEN** resolved review event onsets fall inside the exported half-open recording-relative sample range
- **THEN** the export includes them as annotations at their sample-derived onset relative to the exported interval
- **AND** events outside that range are omitted.

#### Scenario: Export long, non-ASCII, or missing channel labels
- **WHEN** a source channel label is longer than 16 characters, contains non-ASCII characters, or is missing
- **THEN** the EEG signal SHALL be written with a stable short ASCII alias
- **AND** the companion label-mapping JSON SHALL preserve the original label value, including `null`, and source channel indexes.

#### Scenario: Reject an interval containing a sample gap
- **WHEN** the complete recording range contains a missing sample-counter range
- **THEN** export fails with a user-readable explanation
- **AND** no synthetic samples or shifted event times are written as a successful export.

#### Scenario: Reject invalid or unavailable export data
- **WHEN** the range is empty, source channels have unsupported units, or a required source read fails
- **THEN** export reports failure and does not leave a partial target file.
