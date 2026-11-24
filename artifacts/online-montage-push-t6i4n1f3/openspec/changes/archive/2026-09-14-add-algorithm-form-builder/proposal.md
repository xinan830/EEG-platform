## Why

The backend can safely store, validate, publish, clone, compare, and preview
algorithm definitions, but research users have no usable interface for those
capabilities. They therefore must edit or inspect implementation details rather
than construct a traceable definition through the product.

## What Changes

- Add a research-only algorithm workbench with a definition list, creation,
  cloning, version browsing, comparison, and publication controls.
- Provide a structured form for input/output metadata, preprocessing,
  windows, metrics, quality rules, and graph nodes, with an advanced JSON view
  synchronized from the same draft state.
- Validate every draft through the backend before save, preview, or publish;
  the browser never decides scientific units, graph validity, or EEG results.
- Create preview executions as explicitly marked, isolated `AnalysisRun`
  records; preview outputs never overwrite a formal result or current Viewer
  and analysis output.
- Retain immutable published versions and make official composite definitions
  read-only where the generic executor cannot faithfully represent them.

No existing EEG viewer, spectrum, spectrogram, analysis, or definition API is
removed. This change does not add a drag-and-drop DAG editor, arbitrary Python,
or clinical interpretation.

## Capabilities

### New Capabilities

- `analysis/algorithm-form-builder`: Research workbench behavior for authoring,
  validating, previewing, publishing, cloning, and comparing definitions.

### Modified Capabilities

- `analysis/run-provenance`: Preview executions become persisted, traceable
  preview AnalysisRuns while remaining isolated from formal outputs.

## Impact

Affected systems are the Definition API/service, Run service and models, Vue
workbench UI, API client/types, backend and frontend tests, and research
architecture documentation. The existing SQLite definition/run tables are
extended only through non-destructive migrations if new preview metadata is
needed.
