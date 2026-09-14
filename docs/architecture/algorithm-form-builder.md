# Algorithm Form Builder

## Scope

Change 05 adds a local research workbench for authoring algorithm definitions.
It is intentionally a form plus advanced JSON editor, not a drag-and-drop DAG
editor or an arbitrary Python runtime. Existing Viewer, PSD, Spectrogram, and
legacy analysis APIs remain unchanged.

## Authoring Contract

The workbench holds exactly one canonical `DefinitionVersionDraft`. The form
projects selected draft fields, while the advanced JSON editor serializes and
parses the same object. Invalid or structurally incomplete JSON remains text in
the editor and does not replace the last valid draft.

The backend is the scientific authority. Before a version can be saved,
published, or previewed, it validates graph topology, node vocabulary, units,
and declared inputs. The frontend uses structured error codes and does not
duplicate unit checks, channel checks, or any EEG calculation.

Definition-version requests use a latest-request guard. If a user selects a
second definition before the first version request returns, the stale response
cannot replace the newer definition's form state.

`GET /api/algorithm-definitions/capabilities` supplies the closed node and unit
vocabulary. Published definitions are immutable. Editing a published version
requires a new SemVer version or a clone.

## Composite Official Definitions

The official RBP definition uses generic research primitives. Official
Theta/Beta, FAA, BrainBeat, and IAPF include composite semantics that the
generic execution engine cannot faithfully represent. The workbench therefore
shows their contracts as inspectable but disables generic editing and execution.
This boundary prevents a simplified user graph from being represented as the
official metric.

Channel roles remain explicit. The workbench does not infer a spatial role
such as logical `Oz` merely because a similarly named raw channel is present.

## Preview Boundary

`POST /api/algorithm-definitions/preview-run` creates a persisted
`AnalysisRun` with `is_preview=true`. It accepts a recording ID, an absolute
requested range, a canonical draft, and typed scalar **simulation** inputs.
The closed backend definition engine evaluates those scalars; the browser only
submits inputs and renders the returned result.

Preview provenance records the source file identity, requested and actual
absolute ranges, draft/configuration digests, implementation environment, and
quality state. A preview does not read raw EEG samples, does not create formal
EEG result artifacts, and cannot overwrite a formal analysis run. An
unavailable output ends as `gate_failed` with `PREVIEW_OUTPUT_UNAVAILABLE` and
`null`, never as a fabricated zero.

## Lifecycle

The workbench supports definition creation, draft version creation, validation,
cloning, publication, version comparison, and scalar preview. Saving or
publishing uses backend validation first. Preview results display their
explicit preview status, unit, quality, and provenance without clinical
interpretation.

## Verification Boundary

This feature proves authoring, validation, and scalar-preview traceability. It
does not claim that a form preview is an EEG measurement or clinical result.
Recording-to-primitive bindings, batch execution, and arbitrary code execution
remain separate roadmap changes.
