## ADDED Requirements

### Requirement: Rebind moved content-identical WPF recordings
When a validated WPF recording matches an existing recording by content identity and the existing source directory is unavailable, registration SHALL update the stored source directory to the submitted directory before resolving the recording catalogue. Registration SHALL preserve the recording identifier and all recording-bound montage snapshots.

#### Scenario: Re-register a moved WPF recording
- **WHEN** a completed WPF recording is registered from a directory whose validated content identity matches an existing recording and the existing directory is missing
- **THEN** the existing recording is rebound to the submitted directory before its catalogue is read
- **AND** registration returns the same recording identifier and its existing montage snapshots

#### Scenario: Keep a still-available source directory
- **WHEN** a content-identical WPF recording is registered from another directory while the existing source directory remains available
- **THEN** registration keeps the existing source directory and recording identity
