## Context

See proposal.md and the four delta specs. The current WPF algorithm workspace registers a
record with one acquisition-montage projection and exposes only its output channel names.
The review display montage already has a separate projection path and must not become the
algorithm input. The new behavior crosses acquisition manifest persistence, backend
registration and sample reading, official Run provenance, and the WPF shared analysis card.

## Goals / Non-Goals

**Goals:**

- Capture a validated, immutable catalogue of selectable montage profiles at acquisition
  time, together with the recorded source-channel mapping and the acquisition default.
- Expose a two-stage WPF selector: historical analysis montage first, role/output channel
  second.
- Resolve the selected output to actual recorded source channels before preprocessing.
- Carry montage identity, fingerprint, role mapping and output formula through all nine
  official algorithm strategies.
- Make a montage change a scientific configuration revision with latest-only request
  handling, cancellation and cache identity separation.
- Keep raw sample chunks immutable and leave algorithm mathematics in backend modules.

**Non-Goals:**

- No arbitrary montage editing from the algorithm page.
- No implicit use of the current global montage store to reinterpret an old recording.
  Explicitly submitted compatible profiles are validated and saved separately as analysis
  snapshots, never represented as acquisition history.
- No coupling to the review page's current viewing montage.
- No change to algorithm formulas, units, quality thresholds or dynamic window mathematics.
- No compatibility calculator for retired algorithm versions.

## Decisions

### Persist a historical montage catalogue at acquisition

The acquisition manifest will retain the selected montage snapshot plus a validated list of
other selectable profiles bound to the same channel-configuration snapshot. Each entry will
include profile identity/revision, semantic fingerprint, derived output definitions, source
native indexes, hardware reference/ground and the selected default. The catalogue is immutable
with the recording. A profile is eligible only when every required source resolves uniquely
to an acquired EEG V signal and its channel configuration identity matches the recorded one.

The backend registration response will expose structured montage descriptors rather than asking
the WPF client to infer them from display names. A legacy record with no catalogue will remain
readable under its recorded/default source where supported, but no alternate montage will be
invented.

### Use a stable role-to-output mapping

The first selector chooses a montage profile. The second selector chooses an output for the
algorithm's declared input role. The role is bound to the historical native channel identity
and source mapping, not to an array position or a display label. When the profile changes, the
same role is resolved to its output in the new profile; if absent, the request is unavailable.

Single-channel algorithms send one mapped output. Paired algorithms send two mapped outputs in
their declared order. The existing algorithm-owned adapters keep their parameter parsing and
result projection; only the common input snapshot and request serializer gain montage fields.

### Project in the backend from immutable source data

The backend sample reader will accept a selected historical montage descriptor and derive V/
float64 output arrays from the immutable raw source channels before shared preprocessing. The
projection fingerprint, formula and source mapping are included in the signal source identity,
Run configuration fingerprint and provenance. Display code never performs this arithmetic.

### Treat montage changes as normal configuration revisions

The WPF ViewModel will keep selected montage, available mapped outputs and selected output roles
as separate properties. A montage change increments the existing configuration revision,
invalidates previews, cancels active on-demand/background work where possible, and schedules
the normal run path. Response handlers compare the revision plus recording, algorithm,
montage fingerprint and output mapping before publishing. The same guard is used by static,
full dynamic, on-demand and combined modes.

### Keep review and analysis independent

Review may continue to project a compatible viewing montage from its own selector. Its cache
keys remain display-oriented. The algorithm request contains only the historical analysis
montage selected in the algorithm detail and never reads the review selector.

### API shape and provenance

Registration returns a stable recording identity and a list of historical montage descriptors.
Official Run config adds analysis_montage_id, analysis_montage_fingerprint, analysis_roles
and resolved output channel names while retaining the algorithm's existing parameters. The
backend rejects a descriptor that is not present in the recording catalogue or whose mapping
does not match the immutable source. Provenance records both requested and resolved mappings,
source unit V, sampling rate, actual range, algorithm identity and implementation identity.

### Explicit compatible analysis snapshots (user-confirmed 2026-10-21)

Records without captured alternate profiles can use explicitly submitted current profiles.
WPF uses the same record-compatibility catalogue as review to select candidates, without
reading review's active selection. Registration carries serialized profile definitions.
The backend independently validates device signature, channel-configuration identity,
hardware reference/ground, unique native/source mappings, formula dependencies and V units.
Accepted definitions are persisted in a separate recording-scoped SQLite table, keyed by
content hash; changed definitions create new identities and cannot overwrite prior snapshots.
Registration/reload merges these descriptors with acquisition history while keeping the
acquisition default selected. Duplicate acquired definitions are omitted. Invalid submitted
definitions reject registration with a structured error, without partially saving candidates.
Readers resolve the selected persisted snapshot before shared preprocessing; provenance
records `snapshot_source=analysis_configuration_snapshot`, the original profile identity and
the content identity. Acquisition snapshots use `snapshot_source=acquisition`. Source channels,
V/float64 values, sample timing, scientific formulas and raw files remain unchanged.

This explicit flow supersedes the acquisition-only restriction above for submitted, validated,
persisted candidates. No alternate is inferred solely from a profile name or channel position.

## Risks / Trade-offs

### On-demand result discovery latency (2026-10-21)

Desktop PSD logs show uncached requests around 700–1000 ms and cached requests
in tens of milliseconds. These are end-to-end observation times, not isolated
scientific computation timings. The observer currently sleeps 500 ms in queued
and 1000 ms in preparing_source, even if the backend becomes ready immediately.
Poll every 100 ms during the first second of observation, then retain the existing
state-specific backoff for long jobs. HTTP requests remain sequential. Enforce
the coordinator's 200 ms minimum interval between observation starts, counting
the time already spent observing and releasing the previous subscription, rather
than adding 200 ms after every completion. Keep awaited bounded cleanup, a single
active observation and latest-pending coalescing. No API shape, scientific range,
sample time, units, channel order, preprocessing or compute concurrency changes.
The brief fast-poll period increases status reads up to ten per second per active
observer; slow preparation backs off after one second. Real desktop playback
latency remains subject to manual acceptance, not inferred from synthetic tests.

### Combined-mode admission correction (2026-10-21)

The on-demand admission query counted queued full-producer publication reservations
against interactive queue, project and recording quotas. A progressing full Run can
retain many such reservations until final artifact publication, incorrectly rejecting
an unrelated cursor window with `WINDOW_CAPACITY_EXCEEDED`. Admission limits must count
queued/running attempts of the requested producer mode. Existing same-window attempts
remain reusable before quota checks, without changing producer ownership or running
leases. The interactive executor remains single-worker and the shared scientific
compute coordinator retains its concurrency/fairness limits; full work remains governed
by the Run scheduler. Raw data, units, channel order, window timing and API shapes do
not change. Genuine interactive saturation still returns the existing structured error.

- [Risk] Manifest size increases when several profiles are captured.
  → Store compact canonical JSON definitions and fingerprints; do not duplicate sample data.
- [Risk] A user profile may be edited after acquisition.
  → Use the immutable snapshot and revision captured in the recording; current profile edits
  never change historical analysis.
- [Risk] A montage change can race with a long Run.
  → Include configuration revision and scientific fingerprint in every publication guard and
  cache key; cancel obsolete work where the backend supports cancellation.
- [Risk] Some profiles may not contain every channel role required by an algorithm.
  → Filter the second selector by declared roles and return structured missing-role reasons;
  never substitute a nearby channel.
- [Risk] Nine algorithms have different input shapes.
  → Keep one shared montage selection contract and algorithm-owned adapters for scalar,
  matrix, paired and multi-value outputs.

## Migration Plan

1. Add manifest and registration schemas, parser validation, and backend projection tests.
2. Persist the catalogue for newly completed acquisitions and return descriptors during WPF
   registration.
3. Add WPF selectors and request serialization behind the validated descriptor contract.
4. Wire revision/cancellation guards through all nine algorithm modes and add provenance checks.
5. Run backend tests, desktop tests/build, manual selector and recomputation acceptance, OpenSpec
   strict validation and git diff checks.
6. Rollback is code/configuration rollback; raw recordings and their immutable manifests are
   not rewritten. New records created without the new catalogue remain explicitly non-switchable.

