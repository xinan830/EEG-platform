# Design

## Ownership

- Python `app.core.logging_setup` owns backend logger configuration.
- `app.core.api_contract` adds request context and HTTP completion fields.
- `PersistentRunQueue` owns Run lifecycle diagnostics; algorithms remain unaware of logging.
- WPF `Infrastructure.Diagnostics.DesktopLog` owns client file output.
- `HttpAlgorithmClient` owns request IDs for backend calls and logs status/latency, without logging request payloads.
- Recording `audit.jsonl` remains the source for sample gaps, lifecycle boundaries and acquisition-specific facts.

## Storage

- Development backend: `<storage-root>/logs/backend-YYYYMMDD.log`.
- Packaged backend: the same `BRAIN_STORAGE_ROOT/logs` location.
- WPF: `%LOCALAPPDATA%/BrainPlatform/logs/desktop-YYYYMMDD.log`.
- Files use UTF-8, append mode, a bounded rotating handler (10 MiB per file, five backups), and are safe when the directory is unavailable by retaining stderr/debug output.

## Correlation and privacy

Each HTTP request keeps or creates `X-Request-ID`; backend completion/error logs include it. Run logs include `run_id`, `recording_id`, `analysis_type`, `stage`, status, progress and elapsed time. WPF logs include request ID and route. Payloads and EEG arrays are excluded. Exception logs include type and traceback, while the API response remains a stable structured error.

## Alternatives and rollback

The alternative of relying on uvicorn stdout is insufficient for packaged WPF runs and is retained only as a fallback. The change is additive: deleting the new logger configuration and WPF logger calls restores previous behavior without changing stored recordings or Runs. Audit files and SQLite audit events are not migrated.

## Testing

Test logger configuration with a temporary directory, request ID propagation, HTTP failure logging, and Run failure traceback/context. Run the backend suite, desktop tests/build, OpenSpec validation, and `git diff --check`.
