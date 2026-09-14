## Context

See `proposal.md` for motivation. The application currently lets multiple services create SQLite tables independently, stores imported files by recording ID, returns analysis results synchronously as JSON, and caches preprocessed signals in memory by recording ID plus algorithm version. There is no authoritative schema version, source-byte hash, run lifecycle, full configuration snapshot, artifact index, or persistent validation record.

The migration must preserve existing local databases and routes. Scientific arrays can be much larger than appropriate SQLite JSON payloads. Source EEG files remain read-only, and viewer processing must stay separate from offline analysis.

## Goals / Non-Goals

**Goals:**

- Establish one idempotent SQLite migration entry point used before services query their tables.
- Make source identity, run configuration, environment, quality, and artifacts traceable end to end.
- Provide a reusable artifact store with atomic writes and integrity checks.
- Add compatible run and validation APIs with stable error codes.
- Keep existing `offline-spectral-v3` numerical behavior unchanged outside the explicitly expanded quality detector.

**Non-Goals:**

- A background worker, retry scheduler, projects, batches, or distributed execution.
- Algorithm definitions or arbitrary node graphs.
- Storing source EEG in SQLite.
- A clinical-validation workflow or clinical claims.

## Decisions

### Use an ordered migration registry and one-row schema metadata table

`app.persistence.migrations` owns ordered migration functions. Startup runs them in `BEGIN IMMEDIATE` transactions and records a monotonically increasing integer in `schema_metadata`. Migrations use schema introspection for legacy databases so rerunning startup is safe.

Alternative considered: continue `CREATE TABLE IF NOT EXISTS` in each service. Rejected because it cannot express column backfill order, version compatibility, or transactional upgrade evidence.

### Keep legacy service initialization as compatibility callers

Services call the centralized migrator before use, then may retain idempotent table creation temporarily. This avoids requiring one global application singleton in tests that instantiate services directly.

Alternative considered: migrate only from `app.main`. Rejected because unit tests and scripts construct services without importing the FastAPI app.

### Compute recording SHA-256 while importing and backfill legacy rows at service startup

New uploads calculate digest and size from the exact byte payload before metadata insertion. A backfill walks only rows missing identity and hashes their stored source when available. Missing legacy files remain nullable and are surfaced as unavailable provenance.

Alternative considered: hash on every run. Rejected because large files would be repeatedly read and cache identity would be unnecessarily expensive.

### Store channel metadata as aligned JSON arrays in Change 01

Raw labels, canonical labels, channel types, and units are stored in separate ordered JSON columns aligned by sample-column index. Canonical labels use trim plus case-preserving deterministic cleanup, while matching remains case-insensitive. A later normalization change may introduce relational channel rows if queries require it.

Alternative considered: a channel table now. Deferred because Change 01 needs provenance, not channel-level querying, and a table would increase migration surface without current product benefit.

### Execute initial Run API synchronously behind a lifecycle record

`POST /api/runs` persists `queued`, transitions to `running`, executes one supported legacy analysis kind, then persists a terminal state. The resource contract is future-compatible with the persistent queue in Change 06, but this change does not falsely claim asynchronous behavior.

Alternative considered: return 202 immediately. Rejected for Change 01 because there is no durable worker yet; presenting asynchronous semantics without a worker would create stuck runs. Change 06 will change creation semantics with its own spec.

### Use normalized JSON plus SHA-256 identities

JSON normalization uses UTF-8, sorted object keys, compact separators, preserved array order, finite numeric values, and explicit nulls. Cache identity hashes a versioned envelope containing source, definition, config, build, and actual range. Scientific definition version and implementation build version remain separate fields.

Alternative considered: hash Python `repr` or raw request JSON. Rejected because ordering and formatting would make equivalent configurations unstable.

### Write artifacts atomically and store relative paths

NPZ bytes are written to a temporary file within the artifact directory, flushed, then replaced into the final run-scoped path. Metadata stores a path relative to the configured storage root and a SHA-256 of final bytes. Reads resolve and verify that paths remain under the artifact root.

Alternative considered: SQLite BLOBs. Rejected because large matrices would inflate database locks, backups, and JSON result reads.

### Model validation as persisted engineering evidence

Validation creation accepts a structured evidence payload or a supported run-artifact comparison, persists normalized summaries and tolerances, and exports a versioned JSON report. Frontend validation remains a read-only consumer.

Alternative considered: persist only PASS/FAIL. Rejected because it omits the tolerance and error evidence required for independent interpretation.

### Add quality detectors without changing valid spectral math

Quality evaluation occurs on each expected segment before Welch averaging. Non-finite, amplitude, low-variation flatline, repeated-limit clipping, and incomplete-length conditions return stable codes. Clean segments continue through the existing Welch path unchanged; rejected arrays remain non-values.

Alternative considered: replace invalid segments with zeros. Rejected because that creates false low-power measurements.

## Risks / Trade-offs

- **[Hash backfill can delay first startup for large legacy collections]** -> Hash only missing rows, commit progress per recording after schema migration, and expose the digest as nullable until available.
- **[SQLite schema changes from multiple service instances can race]** -> Use an immediate transaction, a short busy timeout, and idempotent migration guards.
- **[Build identity derived from Git may be unavailable in packaged deployments]** -> Prefer an explicit environment/build constant, then package version, and record a stable `development-unversioned` fallback rather than time-based identity.
- **[NPZ is Python-oriented]** -> Pair every artifact with media type, unit, shape, and a JSON index; Change 07 adds portable CSV/JSON export.
- **[Cancellation cannot interrupt synchronous numeric code immediately]** -> Permit cancellation only before execution in Change 01 and return conflict for terminal/running work; cooperative worker cancellation belongs to Change 06.
- **[Expanded quality rules can reject data previously accepted]** -> Version quality-rule configuration in provenance, test each detector independently, and retain the mathematical golden tests for clean data.

## Migration Plan

1. Add migration infrastructure and tests for empty, legacy, repeated, and failed upgrades.
2. Add recording columns and backfill source/channel identity without changing existing response fields.
3. Add run, artifact, and validation tables plus services and typed models.
4. Add quality reason detectors and regression tests proving clean golden values remain unchanged.
5. Register new routers after existing routes and add API contract tests.
6. Run full backend and frontend regression gates and generate the Change 01 validation report.
7. If startup migration fails, stop application initialization and retain the prior schema version. Code rollback remains able to read legacy columns; new additive tables/columns can remain unused.

## Testing Strategy

- Migration tests: existing fixture database, data preservation, repeat execution, transactional failure, and old-result reads.
- Identity tests: exact SHA-256/size, canonical JSON stability, cache invalidation for each key component, and channel order.
- Artifact tests: compressed-array round trip, atomic metadata, path confinement, and corruption detection.
- Run API tests: lifecycle states, legacy compatibility, structured not-found/conflict errors, provenance completeness, and cancellation behavior.
- Validation tests: PASS/FAIL calculations, JSON report schema, environment capture, and engineering-only wording.
- Spectral tests: all quality reason codes, null semantics, clean-segment averaging, known sine power, existing golden values, and spectrogram/static parity.
