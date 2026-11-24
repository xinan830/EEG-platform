## Context

See `proposal.md` for motivation.  The present application has a strong
spectral foundation but layers official execution onto it with a separate
`OfficialAlgorithmRunConfig`, an official catalog, role mapping on
`RecordingSummary`, `RunService` ID branches, and executor ID branches.
`metric_values` in the Theta/Beta module contains unrelated historical
three-channel outputs.  The frontend duplicates official identity checks when
creating Runs and selecting output units.

Run rows and artifacts are already persisted with result summaries and
provenance snapshots.  Therefore historical output can remain readable without
keeping historical calculators importable.

## Goals / Non-Goals

**Goals:**

- Give every executable algorithm one module-owned manifest, configuration
  model, parameter schema, input resolver, static/dynamic executor, result
  serializer, and validation suite.
- Make generic Run services dispatch through a registry rather than concrete
  algorithm IDs.
- Make raw imported labels the only default channel choices; require every
  multi-channel relationship explicitly per Run.
- Replace new Theta/Beta results with one chosen raw channel and a v2
  scientific identity while retaining frozen spectral and IAPF primitives.
- Preserve historical Run/Artifact inspection and exports after code removal.

**Non-Goals:**

- Changing `offline-spectral-v3`, IAPF 1/f/Peak/COG methods, units, Welch
  behavior, quality criteria, viewer filtering, or spectrogram mathematics.
- Adding arbitrary Python user plugins, clinical interpretation, remote
  execution, or a drag-and-drop DAG editor.
- Treating O2 as Oz, inferring a channel role from order, or silently
  substituting any channel.

## Decisions

### One runtime contract, module-owned algorithms

Create `app/algorithm_runtime/` for framework concerns and
`app/algorithms/<id>/` for scientific product algorithms.  An algorithm
implements a manifest, Pydantic config model, client-safe parameter schema,
input resolution, static and dynamic execution, and result serialization.
The runtime validates the request, invokes the selected module, and hands its
normalized result to existing Run/Artifact persistence.

The registry is the sole executable truth.  It has an official provider and a
user-definition provider; user graph evaluation stays backend-only but is
wrapped into the same result and catalog contracts.  This choice avoids a
second full runtime for user algorithms while keeping their persisted Definition
versions immutable.

Alternative considered: retain `official_algorithm` and `definition_metric`
branches but move only their helper code.  Rejected because it leaves the
generic service as an algorithm integration point and guarantees duplication
as more algorithms are added.

### Raw channel values replace global role mapping

The import process continues to persist ordered raw labels, canonical labels,
types, and units.  `ChannelMapping` is removed from the recording model,
metadata payload, UI, API, and database.  A parameter schema may declare
`channel`, `left_channel`, `right_channel`, or any other explicit raw-label
input.  Validation checks selected labels exist in the recording.

Alternative considered: retain mapping only internally.  Rejected because the
old global relationship is precisely what causes a one-channel algorithm to
appear to consume Fz/Pz/Oz and lets stale UI state affect a Run.

### Theta/Beta v2 is a new scientific contract

`official-theta-beta-v2` accepts one raw channel, requested range, mode, and
for dynamics a trailing window plus refresh step.  It loads the frozen PSD,
estimates IAPF from that exact channel, integrates Theta
`[max(4, IAPF-6), IAPF-2]` and Beta `[IAPF+2, 30]`, and returns Theta/Beta as
a dimensionless ratio.  It returns `null` with stable reasons for quality,
IAPF, or denominator failure.  A dynamic series stores actual bounds for each
window.  It never computes, averages, displays, or infers Fz/Pz/Oz roles.

The existing three-role Theta/Beta calculation is not migrated as an
executable capability.  Its historic outputs remain in existing artifacts;
the unused code is deleted after baseline evidence is recorded.

### Parameter schema and display-state boundary

The backend catalog returns parameters with `key`, Chinese `label`, type,
allowed values/defaults, required/visibility constraints, unit, and
`affects_science`.  Input channel, requested range, dynamic window, and step
are persisted in a Run.  Chart history/zoom/collapsed debug panels are local
frontend state and are not submitted.  The backend, not Vue, validates all
scientific values.

### Storage and migration ownership

SQLite remains responsible for recordings, definitions, Run metadata, and
result summaries; artifact files remain responsible for numeric arrays.
Migration removes mapping storage only after the new raw-channel flow has
replaced every production reader.  Migration must be transactional and
idempotent.  Historical Run `channel_mapping_json` is retained as a serialized
snapshot because it is evidence, not active recording metadata.

### API cutover

`GET /api/algorithms` becomes the only algorithm chooser catalog, returning
both official and user entries.  `POST /api/runs` accepts one runtime algorithm
request shape with a catalog algorithm identity/version and algorithm config.
Old official execution request forms, the mapping endpoint, and any legacy
algorithm-entry API are removed in this approved breaking change.  Spectrum,
spectrogram, waveform, and read-only historical Run endpoints remain.

## Risks / Trade-offs

- [Migration removes an active mapping consumer] → Perform a repository
  call-graph audit first; delete only after replacement tests and a startup
  migration test demonstrate no runtime lookup remains.
- [Historic artifact lacks enough display metadata] → Historical views use the
  persisted result summary and provenance verbatim; show unavailable fields as
  unavailable rather than reconstructing with deleted algorithms.
- [New runtime subtly changes spectral results] → Use existing frozen spectral
  fixtures and add pointwise IAPF/Theta/Beta v2 golden tests before cutover.
- [Schema-driven UI becomes too generic for a complex algorithm] → Permit a
  small registered form extension, but keep channel/time/submission/result
  contracts in the common form renderer.
- [Breaking old clients] → This is intentional for old algorithm and mapping
  entry points.  Return structured removal codes during the release window and
  update this local frontend in the same change.

## Migration Plan

1. Capture current spectra, IAPF, legacy Theta/Beta evidence, database shape,
   and consumer imports in tests before moving code.
2. Add the runtime and new algorithm modules beside current code; prove their
   outputs against frozen primitive-level tests.
3. Switch new Run creation, catalog reads, and the local frontend to the
   runtime request/catalog contract.
4. Migrate recording metadata to remove active mapping fields; retain only
   historic serialized Run snapshots.
5. Delete compatibility facades, old executable entry paths, mapping API/UI,
   and concrete-ID branches.  Add architecture tests preventing reintroduction.
6. Run migrations against current and already-migrated SQLite fixtures, then
   run all tests/builds and archive the OpenSpec change.

Rollback before deletion is a Git rollback plus a database backup restore.
After a released migration, rollback does not recreate global mapping data;
historical records remain readable and a new forward migration is required for
any future schema change.
