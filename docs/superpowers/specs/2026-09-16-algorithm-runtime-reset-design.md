# Algorithm runtime reset and Theta/Beta v2 design

## Decision and scope

This change replaces the current official-algorithm execution integration with
one clean algorithm runtime. It removes compatibility facades, legacy
algorithm-entry APIs, duplicate execution paths, and global semantic channel
mapping as a prerequisite for analysis.

Historical Runs, Artifacts, result summaries, and provenance snapshots remain
readable and exportable. They are immutable evidence only: the system will not
re-run, clone, or extend an old algorithm version. No historic result requires
old Python algorithm code at read time.

The change does not alter the frozen `offline-spectral-v3` preprocessing,
Welch PSD, frequency boundaries, units, or quality rules unless an algorithm's
new scientific version explicitly says otherwise.

## Problem

The current system has a solid spectral and Run foundation, but its official
algorithm integration is only partly modular.

- Formula modules exist under `eeg_core/official_algorithms`, but generic Run
  services branch on concrete algorithm IDs.
- `theta_beta.py` currently returns a bundle of unrelated legacy metrics,
  rather than a dedicated Theta/Beta result.
- Static/dynamic request validation, input selection, output formatting, and
  UI assumptions are split across `runs.py`, `run_analysis_executor.py`,
  frontend components, and model files.
- The old Theta/Beta contract requires a global Fz/Pz/Oz mapping and exposes
  three roles even when a user selected one source channel.
- Compatibility modules obscure which algorithm implementation is canonical.

These boundaries make adding an algorithm require edits to multiple unrelated
generic files and expose backend implementation details to users.

## Target architecture

```text
signal/
  preprocessing, spectral, quality, units, scientific types
        ↓
algorithms/<algorithm-id>/
  manifest, config, compute, runner, validation
        ↓
algorithm_runtime/
  registry, contracts, parameter-schema, executor, windows, results, errors
        ↓
services/
  Run lifecycle, recording access, Artifact persistence, result retrieval
        ↓
api/
  algorithm catalog and Run endpoints
```

### Signal layer

The signal layer owns reusable scientific primitives only: signal loading,
offline preprocessing, PSD estimation, band integration, quality gates, units,
and typed intermediate values. It must not know individual product algorithms.

### Algorithm module

Each official algorithm has a single dedicated package. A package owns:

- a stable algorithm ID, scientific version, implementation identity, Chinese
  display text, and output description;
- a Pydantic configuration model and a frontend-safe parameter schema;
- input resolution from a recording and an explicit per-run configuration;
- pure computation functions;
- static and dynamic runner methods;
- algorithm-specific validation and golden/reference tests.

No algorithm package imports HTTP routes, Vue concerns, SQLite repositories, or
another algorithm's product-level runner.

### Algorithm runtime

The runtime is the only route from an algorithm request to an execution. It
loads an explicit algorithm from the registry, validates its configuration,
passes recording access through a bounded input context, executes it, and
normalizes the result to the Run/Artifact contract.

Generic runtime and service code must not branch on an algorithm ID. Adding an
algorithm means adding its own package and registering one object; it must not
require edits to the generic Run resolver or executor.

### Services and APIs

Run services retain lifecycle state, idempotency, cache identity, cancellation,
provenance, Artifact writes, and historical result retrieval. They do not own
scientific formulas.

The API exposes one catalog endpoint and one Run creation shape. The catalog
returns backend-authored parameter and result schemas. The frontend formats and
submits values but performs no scientific validation or calculation.

## Common algorithm interface

Every executable algorithm implements the conceptual interface below. Exact
Python names may vary, but these responsibilities may not be split across
generic services.

```python
class Algorithm:
    manifest: AlgorithmManifest
    config_model: type[BaseModel]

    def parameter_schema(self) -> ParameterSchema: ...
    def resolve_inputs(self, recording, config) -> AlgorithmInputs: ...
    def execute_static(self, inputs, config) -> AlgorithmResult: ...
    def execute_dynamic(self, inputs, config) -> AlgorithmSeriesResult: ...
```

The parameter schema declares label, input type, allowed values, default,
required state, visibility rule, unit, and whether a value affects science or
only display. Backend Pydantic validation remains authoritative.

## Parameter and display boundary

Scientific calculation parameters and client presentation parameters are
separate by contract.

| Category | Examples | Persisted in Run configuration | Changes numerical result |
| --- | --- | --- | --- |
| Input | raw channel, left/right pair | Yes | Yes |
| Analysis | static/dynamic mode, requested range, trailing window, step | Yes | Yes |
| Display | result chart history, collapsed evidence panels | No; local UI state | No |

This prevents a result-chart history choice from changing analysis windows, and
prevents an algorithm-specific window from unexpectedly moving a waveform or
spectrogram viewport.

## Theta/Beta v2

`official-theta-beta-v2` replaces the current multi-role official execution
for all new Runs.

### Inputs

- One user-selected, real raw recording channel.
- Static or dynamic analysis mode.
- Requested time range.
- For dynamic mode: trailing analysis window and refresh step.

The algorithm does not use a global recording mapping and does not relabel raw
channels. Selecting `O2` records and displays `O2`, not `Oz`.

### Calculation

For each static range or dynamic trailing window:

1. Compute the existing offline PSD for the chosen channel.
2. Estimate IAPF from that same channel's PSD using the frozen 1/f, Peak/COG
   contract.
3. Integrate Theta over `[max(4, IAPF - 6), IAPF - 2]` Hz.
4. Integrate Beta over `[IAPF + 2, 30]` Hz.
5. Return `Theta power / Beta power` as a dimensionless ratio.

If PSD quality fails, IAPF is unavailable, or a denominator is invalid, the
result value is `null` with structured reasons. It is never replaced with zero.

### Output

Each result has one selected source channel and one scalar/series result. The
main UI reads, for example:

```text
Theta/Beta 比值 · Fz
当前值：1.42
实际分析区间：12.000–22.000 s
质量：Clean
```

The old `Fz/Pz/Oz` output and implicit display of Fz are removed.

## Channel selection and mapping

Raw recording channel labels are extracted at import and are the default input
choices for every algorithm.

Global `ChannelMapping`, the mapping endpoint, mapping UI, automatic mapping,
and `OFFICIAL_CHANNEL_MAPPING_REQUIRED` are removed if the audit confirms no
remaining production workflow requires them. Algorithms that need multiple
signals declare explicit per-run parameters instead:

```text
FAA: left_channel and right_channel
BrainBeat: frontal_channel and posterior_channel
```

No algorithm may infer a semantic role from channel position or alias.

## Catalog and UI contract

`GET /api/algorithms` returns official and user algorithms in one catalog with
an explicit source (`official` or `user`). Each catalog item includes its
parameter schema, supported modes, output schema, versions, availability, and
plain-language description.

The UI consists of:

1. an algorithm picker with no parameters or result charts embedded in it;
2. one independent configuration card per selected algorithm;
3. a common Run action that submits each selected algorithm's own scientific
   parameters;
4. result cards that show the actual selected raw channel and backend result.

Complex algorithms may supply an algorithm-specific form extension, but common
channel, time, dynamic-mode, validation, submission, and result behaviors stay
schema-driven.

## Legacy removal

The implementation starts with an import/call-graph audit. Candidate legacy
items include `offline_metrics.py`, `official_definitions.py`,
`official_algorithm_shadows.py`, `eeg_core/faa.py`, `processing/offline_analysis.py`,
the current official Run config, the recording mapping feature, and
algorithm-specific conditionals in generic Run services.

For each candidate, the audit classifies it as:

- current mathematics to move into a canonical algorithm package;
- current runtime dependency to move into `signal` or `algorithm_runtime`;
- historical read-only serialization support;
- obsolete compatibility/dead code to delete.

No forwarding facade is retained after cutover. The deleted source paths are
guarded by tests so they cannot be silently reintroduced.

## Historical result policy

Historical records remain readable from persisted summaries and Artifacts. A
historical Run displays its stored algorithm ID, version, input snapshot,
quality, and Artifact metadata. It cannot enter the new runtime.

The new runtime only accepts current registered algorithm versions. Re-running
a historical scientific question creates a new Run under a new algorithm
version, never an implicit legacy execution.

## Migration and cutover

Work occurs on one isolated branch and one OpenSpec change:

```text
replace-algorithm-runtime-and-theta-beta-v2
```

Implementation may proceed in small commits on that branch, but `main` receives
one clean runtime, not a permanent dual path. Before removal, a migration
backfills or preserves only the fields needed for historical read-only views.
Database columns used solely by global mapping are removed transactionally only
after tests prove historical Run retrieval does not query them.

## Verification

Required evidence before cutover:

- synthetic and local anonymous-data golden tests for IAPF and Theta/Beta v2;
- static/dynamic equivalence at matching windows;
- selected-channel preservation for Fz, Pz, O2, and arbitrary valid labels;
- `null` result behavior for quality/IAPF/denominator failures;
- parameter-schema validation in backend and form rendering in frontend;
- architecture tests proving generic runtime/services contain no algorithm-ID
  branches and deleted legacy paths are absent;
- historical Run and Artifact read-only retrieval tests;
- SQLite migration, migration-repeat, and rollback tests;
- full backend pytest, frontend Vitest, production build, OpenSpec strict
  validation, and `git diff --check`.

## Explicit non-goals

- No arbitrary Python user plugins.
- No clinical diagnosis or conclusion generation.
- No changes to the frozen spectral mathematics unless separately versioned.
- No global channel-role inference.
