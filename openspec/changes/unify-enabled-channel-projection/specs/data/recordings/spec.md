# Spec Delta

## ADDED Requirements

### Requirement: Bind import-time channel configuration only on an exact unambiguous match
An EDF/BDF recording MAY persist the currently selected channel configuration as display metadata only when every source EEG channel maps one-to-one to a configured EEG input by normalized exact label. The binding SHALL include the file-column to native-channel identity mapping and SHALL not alter source data or algorithm channel choices.

#### Scenario: Every EEG label matches one configured input
- **WHEN** each imported EEG channel has exactly one normalized exact-label match in the selected configuration and no configured input is matched more than once
- **THEN** the recording stores that immutable configuration snapshot and the ordered file-column to native-channel mapping
- **AND** later display projection can filter disabled channels using that import-time snapshot
- **AND** original labels, samples, and file column order remain unchanged

#### Scenario: A source EEG label is missing or ambiguous
- **WHEN** any source EEG channel has no exact match or more than one possible configuration match
- **THEN** the importer stores no configuration binding
- **AND** recording metadata and displays retain the source-file channels in source order

#### Scenario: Configuration changes after import
- **WHEN** the selected channel configuration is later edited or replaced
- **THEN** the imported recording continues to use its persisted import-time snapshot
- **AND** raw samples and source-order backend algorithm inputs remain unchanged

### Requirement: Restore verified platform export channel identity on import
A platform-exported EDF/BDF MAY restore its original display labels and native channel identities only from a matching export sidecar whose SHA-256 identifies the exact uploaded signal file. The importer SHALL validate every EEG column against the ordered sidecar mapping and immutable channel snapshot, and SHALL preserve original file labels, values, and column order as the raw source.

#### Scenario: Exported labels were shortened or aliased
- **WHEN** an EDF/BDF has a valid adjacent export sidecar whose signal digest matches and whose channel map matches the file header
- **THEN** the import stores the sidecar's immutable configuration and source-column/native-index map
- **AND** review uses the original electrode labels from that snapshot while raw reads retain the EDF/BDF labels and column order

#### Scenario: Sidecar does not match the uploaded signal
- **WHEN** sidecar metadata is supplied but its digest, channel order, or configuration binding is invalid
- **THEN** import fails with a structured metadata error
- **AND** it does not silently attach another configuration or display mapping

#### Scenario: Imported analysis values have a declared unit
- **WHEN** an imported recording is prepared for EEG analysis
- **THEN** the prepared-source contract declares `V` for numeric EEG arrays read from the backend
- **AND** original file units remain separate import provenance
