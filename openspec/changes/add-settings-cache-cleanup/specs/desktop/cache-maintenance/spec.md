## ADDED Requirements

### Requirement: Settings exposes rebuildable-cache cleanup
The desktop SHALL provide a single asynchronous clear-cache action in Settings, show progress and completion/failure, prevent repeated submissions, and display the deleted-window count and released file bytes after success. It SHALL clear local review display/source cache directories only after backend cleanup succeeds. Active recording access SHALL block local cleanup and new recording access SHALL wait until cleanup finishes.

#### Scenario: Operator clears idle playback caches
- **WHEN** the operator selects clear cache with no open recording access or backend analysis work
- **THEN** the current backend's dynamic cache and the desktop user's two review caches SHALL be cleared
- **AND** the page SHALL report completion and the next review SHALL regenerate derived data

#### Scenario: A recording remains open or a second click occurs
- **WHEN** a recording is open or cleanup is already running
- **THEN** cleanup SHALL be refused or the repeated action disabled without deleting files

### Requirement: Dynamic records and files share a maintenance boundary
POST /api/maintenance/cache/clear SHALL clear only the four dynamic-analysis tables transactionally before removing their derived JSON/NPZ files. Cleanup SHALL exclude competing storage/analysis HTTP requests and recovery and reject queued/running analysis work. It SHALL reset artifact read buffers and byte accounting, return deleted row/file counts and freed bytes, and use structured errors. No API accepts caller-supplied paths or SQL.

#### Scenario: Stored windows reference missing files
- **WHEN** cleanup finds ready-window records with missing artifacts
- **THEN** it SHALL remove those records and succeed without attempting to decode the missing results
- **AND** a subsequent ensure SHALL create a new window rather than reuse the missing artifact
- **AND** the four cleared tables SHALL be dynamic_analysis_subscriptions, dynamic_analysis_attempts, dynamic_analysis_windows and dynamic_analysis_sessions

#### Scenario: Request or worker is active
- **WHEN** cleanup overlaps another storage/analysis backend request or queued/running analysis work
- **THEN** it SHALL return CACHE_BUSY without deleting records or files

#### Scenario: Live acquisition continues during maintenance
- **WHEN** live-filter or live-recording batch requests overlap cache cleanup
- **THEN** those independent requests SHALL remain available
- **AND** maintenance SHALL preserve their transient session state

#### Scenario: Artifact removal fails after records are cleared
- **WHEN** a file cannot be removed
- **THEN** cleanup SHALL report CACHE_FILES_REMAIN with an explicit partial-cleanup message
- **AND** the removed records SHALL remain absent so no READY index references a deleted file
- **AND** a later cleanup SHALL be able to remove the remaining orphan files

### Requirement: Maintenance preserves scientific and historical ownership
Cleanup SHALL preserve raw recordings, historical analysis Runs and their artifacts, audit logs, project/user configuration, montage snapshots and SQLite schema/index definitions. It SHALL change no time basis, sample range, channel order, unit, quality or provenance semantics. It SHALL log request identity and cleanup counts. Deletion SHALL be restricted to owned cache directories and reject links/reparse points to other data.

#### Scenario: Historical and raw data coexist with caches
- **WHEN** cleanup succeeds
- **THEN** protected table rows and source/historical files SHALL remain unchanged
- **AND** only the connected backend's dynamic cache and fixed desktop review cache directories SHALL be removed
