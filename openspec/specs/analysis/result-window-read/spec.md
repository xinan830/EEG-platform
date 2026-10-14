# analysis/result-window-read Specification

## Purpose
TBD - created by archiving change link-offline-review-psd. Update Purpose after archive.
## Requirements
### Requirement: Read saved results by bounded backend window
The result service SHALL provide a read-only bounded-window view of completed
results without scientific recomputation. Existing result APIs and artifacts
SHALL remain compatible. Limits SHALL constrain reading and conversion work,
not merely the serialized response after loading an entire matrix.
The endpoint SHALL return saved algorithm outputs and window coordinates, not
raw EEG waveform samples. Reading SHALL NOT alter preprocessing context or
recompute a short-window spectrum.

#### Scenario: Read a window of a long recording
- **WHEN** a cursor requests one saved PSD window
- **THEN** the service returns only that window's declared arrays and evidence
- **AND** memory, temporary storage and concurrency follow explicit resource bounds
- **AND** it does not load a full recording matrix merely to slice the response.

### Requirement: Preserve scientific identity and integrity in window views
A window response SHALL retain Run and source identity, algorithm and
implementation versions, ordered source channels, configuration provenance,
actual sample/time ranges, units, quality and stable failure reasons.
Derived indexes SHALL be versioned, atomically published and rebuildable without
rewriting source recordings or historical artifacts.

#### Scenario: A derived index is incomplete or incompatible
- **WHEN** a window reader encounters an incomplete or mismatched index
- **THEN** it rejects that index and rebuilds or reports an explicit preparation/failure state
- **AND** it never serves partial values as a completed scientific result.

#### Scenario: No window ends before the cursor
- **WHEN** the cursor precedes the first eligible backend window
- **THEN** the service returns an explicit insufficient-data state
- **AND** it does not select a future window or invent numeric zero.

#### Scenario: Read one prepared PSD window
- **WHEN** `GET /api/runs/{run_id}/window-preview` receives exactly one of
  `cursor_sample` or `cursor_s`
- **THEN** the response echoes the resolved relative sample and derived seconds,
  the selected window's half-open sample/time range, status, quality reasons,
  units, and saved arrays
- **AND** it returns `RESULT_WINDOW_INDEX_UNAVAILABLE` when the historical
  artifact has not got a compatible derived index
- **AND** it never returns raw EEG samples.

#### Scenario: Invalid cursor coordinates
- **WHEN** both cursor coordinates or neither coordinate is supplied, or the
  coordinate is non-finite, outside the recording, or not an integer sample
- **THEN** the API returns `RESULT_WINDOW_UNAVAILABLE` with a request id
- **AND** it does not choose a nearest or future window.
