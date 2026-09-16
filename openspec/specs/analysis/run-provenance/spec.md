# Analysis run provenance

## Purpose

Provide a durable, deterministic record of every scientific analysis execution so results can be reproduced, audited, cancelled, cached, and traced to immutable inputs and artifacts.
## Requirements
### Requirement: Persist analysis run lifecycle

The system SHALL assign every new run an immutable identifier and persist
exactly one lifecycle status from `queued`, `running`, `completed`,
`gate_failed`, `failed`, or `cancelled` together with creation and update
timestamps. A preview run SHALL use the same lifecycle and set
`is_preview=true`; it SHALL remain isolated from formal result paths.

#### Scenario: Complete a synchronous run

- **WHEN** a compatible analysis finishes successfully
- **THEN** the run transitions through persisted lifecycle state to `completed` and exposes its result summary

#### Scenario: Complete a preview run

- **WHEN** a validated scalar definition preview finishes successfully
- **THEN** it persists `is_preview=true`, a preview-only result summary, and does not overwrite a formal run or artifact

#### Scenario: Reject by quality gate

- **WHEN** scientific quality rules reject the available input
- **THEN** the run ends as `gate_failed`, retains structured quality reasons, and does not report rejected numerical output as zero

### Requirement: Capture reproducibility provenance

Each runtime algorithm Run SHALL persist the selected algorithm ID, source,
scientific version, implementation identity, configuration digest, explicit raw
input channels, requested range, actual range, window semantics, units, and
quality result.

#### Scenario: Inspect a completed result

- **WHEN** a client retrieves a completed runtime Run
- **THEN** the response contains the source identity, executed configuration,
  scientific contract, implementation, units, quality state, and actual range

#### Scenario: Read a completed Theta/Beta v2 Run

- **WHEN** a completed Theta/Beta v2 Run is retrieved
- **THEN** provenance identifies `official-theta-beta-v2`, the selected raw
  channel, one actual analysis range per output point, and the frozen spectral
  contract used to obtain its PSD

### Requirement: Generate deterministic cache identity

The system SHALL derive cache identity from source-file digest, algorithm-definition digest, normalized configuration digest, implementation build version, and actual absolute time range.

#### Scenario: Repeat an identical execution

- **WHEN** source bytes, definition, resolved configuration, implementation build, and actual range are identical to a completed run
- **THEN** the system returns the same cache identity and MAY reuse the existing immutable artifacts

#### Scenario: Change implementation output behavior

- **WHEN** an implementation fix can change numerical output without changing the scientific definition version
- **THEN** a different implementation build identity produces a different cache identity

### Requirement: Store large arrays as immutable artifacts

Large numerical arrays SHALL be stored as compressed NPZ files outside SQLite. Artifact metadata SHALL include identifier, run identifier, kind, relative path, media type, byte size, SHA-256, units, shape summary, and creation timestamp.

#### Scenario: Persist a PSD result

- **WHEN** a run produces frequency and PSD arrays
- **THEN** arrays are written atomically to a derived-artifact path, their SHA-256 is recorded, and SQLite/JSON stores only scalar summaries and artifact indexes

#### Scenario: Detect artifact corruption

- **WHEN** an artifact's bytes no longer match its recorded SHA-256
- **THEN** artifact verification fails with a structured integrity error rather than returning it as valid output

### Requirement: Provide run resources

The API SHALL provide create, retrieve, list, cancel, and artifact-list operations under `/api/runs` while retaining the legacy synchronous analysis API.

#### Scenario: Create a run

- **WHEN** a valid run request is posted
- **THEN** the service creates and executes the run, returns its stable identifier and current status, and does not require the client to use the legacy result route

#### Scenario: Cancel a terminal run

- **WHEN** cancellation is requested for a completed, failed, gate-failed, or already cancelled run
- **THEN** the service returns a structured conflict and does not rewrite terminal provenance

### Requirement: Return stable structured errors

Run APIs SHALL use stable machine-readable error codes with a request identifier and SHALL NOT require clients to parse localized messages.

#### Scenario: Request an unknown run

- **WHEN** a run identifier does not exist
- **THEN** the API returns HTTP 404 with a stable `RUN_NOT_FOUND` code and request identifier

### Requirement: Preserve unavailable value provenance

The provenance of a runtime output with no scientific value SHALL retain its
structured quality or calculation reason while its numerical field is `null`.

#### Scenario: Read a rejected output

- **WHEN** a result has no valid Theta/Beta value
- **THEN** its persisted value is `null` and its reason is distinguishable from
  measured zero
