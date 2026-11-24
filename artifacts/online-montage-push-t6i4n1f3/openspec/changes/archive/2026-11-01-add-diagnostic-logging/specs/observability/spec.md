# Observability

## ADDED Requirements

### Requirement: Persist bounded backend diagnostics

The backend SHALL write structured diagnostic records to a date-partitioned,
bounded log file under the configured storage root, while retaining stderr
output for development. Records MUST NOT contain raw EEG arrays, credentials,
tokens, or patient content.

#### Scenario: Backend starts with a writable storage root

- **WHEN** the application starts
- **THEN** it creates the log directory and writes request, lifecycle, warning,
  and exception records to the current date's backend log

#### Scenario: Storage log directory is unavailable

- **WHEN** the log file cannot be opened
- **THEN** the application remains usable and emits the diagnostic through its
  configured fallback handler without changing scientific behavior

### Requirement: Correlate HTTP diagnostics

Each HTTP request SHALL preserve an incoming `X-Request-ID` or create one, and
completion/error records SHALL include that identifier, method, route, status,
and elapsed milliseconds.

#### Scenario: Client supplies a request ID

- **WHEN** a request includes `X-Request-ID: example-42`
- **THEN** the response header and all request-level diagnostic records use
  `example-42`

### Requirement: Trace analysis Run lifecycle

The backend SHALL log Run ID, recording ID, analysis type, stage, progress and
terminal outcome for queued, running, cancelled, completed and failed Runs.
Failure records SHALL include exception type and traceback in the diagnostic log
while retaining stable API error codes for clients.

#### Scenario: Algorithm execution fails

- **WHEN** a worker catches an unexpected execution exception
- **THEN** the Run remains a structured failed Run and the log contains its
  identifiers, stage, exception type and traceback

### Requirement: Trace desktop requests

The WPF client SHALL write bounded diagnostic records to its local application
log and SHALL send a request ID with backend HTTP calls. Request payloads and
raw signal values MUST NOT be written.

#### Scenario: Backend request returns an error

- **WHEN** an algorithm or recording request returns a non-success status
- **THEN** the desktop log contains request ID, route, status and elapsed time,
  while the user-facing exception remains localized and structured
