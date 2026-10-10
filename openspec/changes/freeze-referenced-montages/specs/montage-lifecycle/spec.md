## ADDED Requirements

### Requirement: Referenced montage immutability
The desktop SHALL prohibit overwriting any montage ID historically referenced by
a recording. It SHALL prohibit deletion while an existing or recoverable trashed
recording references the ID, and allow deletion after the last discoverable
reference is permanently removed and verified. It SHALL allow inspection, copying
to a new ID and archival. Historical edit locks SHALL persist independently of
the original recording's current location and SHALL prevent ID reuse.

#### Scenario: Edit a previously acquired montage
- **GIVEN** a recording references montage A.1
- **WHEN** a user opens A.1
- **THEN** all draft fields are read-only
- **AND** saving the same ID is rejected at the store boundary
- **AND** deleting the same ID is rejected while the recording still exists
- **AND** copying creates an independently editable ID without changing A.1

#### Scenario: Discover historical usage
- **WHEN** a known project contains a legacy recording or a recoverable trashed recording
- **THEN** its saved montage ID is locked without rewriting the recording
- **AND** unreadable reference metadata prevents unsafe preset mutation

### Requirement: Recording freeze boundary
The writer SHALL freeze the selected montage before publishing a recording
manifest. It SHALL reject a stale selected snapshot that conflicts with the
current saved preset under the same ID.

#### Scenario: Preset changes after preview preparation
- **GIVEN** preview was prepared with A.1 and the unused preset was subsequently modified
- **WHEN** recording starts using the stale snapshot
- **THEN** the start is rejected before publishing a manifest

#### Scenario: Preset is deleted after preview preparation
- **GIVEN** preview was prepared with a user preset that is subsequently deleted
- **WHEN** recording starts using the stale snapshot
- **THEN** the start is rejected before publishing a manifest or locking that ID

#### Scenario: Metadata and snapshot identities conflict
- **WHEN** the selected montage metadata ID differs from its snapshot ID
- **THEN** recording reference registration is rejected without locking either ID

### Requirement: Historical snapshot and review projection remain separate
The WPF review workspace SHALL default to the recording's validated acquisition
montage snapshot. Alternate display montages SHALL continue to require the same
channel configuration ID and existing scientific mapping compatibility. Selecting
an alternate SHALL NOT replace the historical snapshot or analysis source.
Missing or invalid snapshots SHALL retain the explicit raw-channel default.

#### Scenario: New copy after acquisition
- **GIVEN** a recording saved A.1 and a compatible A.2 is subsequently created
- **WHEN** the recording is reviewed
- **THEN** the initial display uses the saved A.1 snapshot
- **AND** a scientifically compatible A.2 bound to the same channel configuration
  can be selected for display without changing A.1 or the playback position

#### Scenario: Equivalent projections share cached samples
- **GIVEN** A.1 and A.2 have identical signal fingerprints but different names
- **WHEN** a cached frame is activated after switching to A.2
- **THEN** the frame identifies A.2 while reusing the same projected samples

### Requirement: Archival does not alter science
An archived montage SHALL remain inspectable and copyable, SHALL NOT be offered
for new acquisition, and SHALL retain its historical signal and appearance fields.

#### Scenario: Archive a used preset
- **WHEN** A.1 is archived
- **THEN** future acquisition choices omit A.1
- **AND** existing recordings retain their A.1 snapshot and waveform projection
