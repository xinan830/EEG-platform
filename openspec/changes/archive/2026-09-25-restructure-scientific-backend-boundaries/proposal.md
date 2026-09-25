# Restructure scientific backend boundaries

## Why

The backend has evolved through several analysis architectures. Its current
Runtime is a sound execution entry point, but scientific ownership is split
across `algorithm_runtime`, `algorithms`, `eeg_core`, and `services`:

- `app/algorithms` contains the current Runtime-facing module manifests,
  configurations, runners, and selected computation adapters.
- `app/eeg_core` contains active spectral mathematics, quality checks,
  primitives, legacy processors, compatibility metadata, historical official
  algorithm implementations, and validation references.
- `app/services` correctly owns orchestration and caching in many places, but
  its spectral and Run services currently import scientific implementation
  types and functions directly.
- repositories remain under `services`, while database migrations are under
  `persistence`.

This is not evidence that existing results are wrong. It is evidence that the
ownership boundary is no longer explicit enough to safely add PSD, STFT,
PeakFrequency, BandRatio, ERP, or topographic analysis without further
duplication.

## Goal

Create one explicit, test-enforced ownership model in which every scientific
formula, algorithm module, execution policy, persistence adapter, and legacy
compatibility path has one home and documented allowed dependencies.

The target backend topology is:

```text
api                 HTTP boundary only
bootstrap           composition root for concrete built-in registration
algorithm_runtime   module registration, validation, static/dynamic dispatch
scientific          reusable signal primitives, preprocessing, quality
algorithms          official algorithm modules only
services            use-case orchestration, caches, queues, artifact workflow
persistence         database access and migrations
models              typed request, response, domain, and persistence contracts
legacy              compatibility adapters and retired implementation paths
```

The final directory names may be introduced incrementally. Ownership and
dependency direction are normative; a cosmetic move alone is not completion.

## Scope

- Freeze new scientific implementation in legacy locations while migration is
  active.
- Establish the target ownership map and allowed dependency directions.
- Add a composition root that wires concrete official algorithms into the
  generic Runtime without making the Runtime depend on concrete algorithms.
- Move active reusable scientific capabilities into one authoritative
  `scientific` boundary, one module at a time, with compatibility adapters.
- Make `algorithms` the only home for active official algorithm modules.
- Keep `algorithm_runtime` as the sole generic execution/registration layer.
- Move repositories progressively to `persistence` without changing database
  schema semantics or historical data.
- Retire new user-defined algorithm execution while preserving a visible,
  read-only catalog entry with `status=retired`, `executable=false`,
  `creatable=false`, and `editable=false`; preserve historical definitions,
  Runs, artifacts, and provenance read-only.
- Add architecture tests that prevent reintroduction of forbidden imports and
  duplicate active implementations.

## Non-goals

- No formula, filter, quality threshold, unit, sample-coordinate, or result
  semantic change as part of the structural migration.
- No user-visible API removal in the first migration phase.
- No migration of desktop WPF acquisition/review implementation in this change.
- No replacement of raw recordings, existing SQLite data, or historical Run
  artifacts.
- No broad folder move merely to match a diagram.

## Scientific-contract impact

The migration preserves current scientific outputs against frozen baseline
fixtures. Each output field uses an explicitly recorded comparison rule:
exact equality for discrete, identifier, unit, coordinate, and provenance
fields; and named `rtol`/`atol` tolerances for floating-point arrays and
derived numeric values. Any intentional change to preprocessing, Welch, units,
quality, time/sample semantics, or algorithm behavior requires a separate
scientific-version change with independent validation; it must never be hidden
inside a file move.

## Compatibility and rollback

Each migration step keeps the old public import/API path as a thin adapter
until all active callers and tests use the new authority. Historical Runs read
stored data without importing retired execution code. A migration step can be
rolled back by restoring the adapter target before a legacy implementation is
deleted. No legacy implementation is deleted until shadow comparison,
architecture tests, full backend tests, and documented acceptance evidence
pass.

## Dependencies

This change depends on the active official-algorithm foundation contract:
`add-official-algorithm-parameter-contract`. Its SI-unit, sample-coordinate,
quality, output-schema, and static/dynamic contracts must be implemented before
the migrated scientific boundary is declared final.
