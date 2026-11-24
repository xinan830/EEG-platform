## Context

See `proposal.md` for motivation. Change 03 supplied an immutable definition
repository and closed graph executor, while Change 04 added official
definitions that truthfully declare composite-only boundaries. The frontend has
no definition client or research workbench, and its existing preview endpoint
is transient scalar evaluation only.

## Goals / Non-Goals

**Goals:**

- Make draft definition lifecycle usable through a dense local-workbench panel.
- Keep one canonical client draft that renders both form fields and JSON.
- Send all validation, unit checks, execution, and numerical output generation
  to the backend.
- Persist an explicitly preview-only AnalysisRun so it has normal provenance
  without entering formal result paths.

**Non-Goals:**

- A drag/drop canvas, arbitrary Python, arbitrary user formula runtime, batch
  execution, PDF export, or a clinical decision interface.
- Replacing frozen public EEG algorithms or merging Viewer/Analysis pipelines.

## Decisions

### Draft is normalized JSON, with a projected form

The browser keeps one `DefinitionVersionDraft` object. The form edits an
intentional subset of the graph vocabulary and serializes the same object;
the advanced editor parses complete JSON back into that object. This avoids
two competing sources of truth. Invalid JSON remains an unsaved text error and
does not alter the last valid draft.

Alternative considered: keep separate form and JSON models with a converter.
That would lose graph details or create synchronization races.

### Backend is authoritative

The UI sends the canonical draft to `POST /validate`, then stores/publishes it
only after the backend accepts it. API error codes drive error states; localized
messages are presentation only. Unit and graph validation are never replicated
in TypeScript.

### Preview has ordinary run provenance but restricted scalar inputs

`POST /api/algorithm-definitions/preview-run` accepts a recording identity,
absolute requested range, a draft, and explicitly typed scalar simulation
inputs. It creates an `AnalysisRun` with `is_preview=true`, uses the normal
run lifecycle and provenance fields, and saves only a scalar output summary.
It is deliberately not an EEG algorithm executor: accepted scalar values are
labelled simulation inputs, while future recording-to-primitive bindings need
a separately specified execution adapter.

Alternative considered: send manually evaluated values from the browser. That
would break the no-frontend-math rule and would have no Run provenance.

### Official composite definitions remain inspectable, not editable

Published official definitions are shown with their version, quality contract,
and composite status. The workbench can clone them into a user draft but does
not claim their composite implementation can run through the generic graph.

## Risks / Trade-offs

- [JSON is expressive but error-prone] -> retain a last-valid draft and show
  parsing errors without silently resetting data.
- [Preview could be mistaken for a measurement] -> every API response and UI
  label includes `preview`; no preview can replace an existing run/artifact.
- [Definition form can expose unsupported nodes] -> source node options from a
  backend capability endpoint, and validate before every state-changing call.
- [User maps spatial roles incorrectly] -> form captures logical-to-raw
  mappings explicitly; it never infers `Oz` from an available channel.

## Migration Plan

1. Add additive preview-run request/response contracts and service tests.
2. Add the capability endpoint and frontend API/types.
3. Add the workbench panel behind a local navigation command without changing
   existing primary analysis panels.
4. Validate existing API and Viewer behavior with full regression suites.

Rollback removes the workbench entry point and stops new preview-run requests;
persisted preview runs remain immutable audit records and are not deleted.
