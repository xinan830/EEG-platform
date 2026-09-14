# Design

## Scope and result semantics

This change is a vertical slice for a saved, user-private scalar definition.
The run request contains one recording, one existing channel, one static time
range of at least four seconds, and an immutable definition ID/version. The
server resolves only the curated feature identifiers emitted by the user
builder: absolute Delta/Theta/Alpha/Beta power (`uV^2`) and relative band power
(`ratio`). It uses the existing offline-spectral-v3 preprocessing and Welch
calculation, never Viewer settings.

The resolver supplies backend `Scalar` values to the existing closed graph
executor. The output must retain the executor's unit and quality state. A
quality-gated spectrum produces `gate_failed` with null numerical output; it
is never changed to zero.

## Persistence and API

`RunCreateRequest.analysis_type` gains `definition_metric`. Its config has
`channel` and absolute `time.start_s/end_s`. A run must identify both a saved
definition and an existing immutable version. The resolved definition version,
feature snapshot, analysis contract, range, channel, quality data, output and
NPZ scalar artifact belong to the normal AnalysisRun provenance and cache key.

The queue remains the only execution owner. Its worker reads the persisted
definition version at execution time and emits a structured failure when the
definition/version, requested channel, feature metadata, or output cannot be
resolved. The normal Run and Result APIs remain the only result-read paths.

## Researcher experience

In the ordinary algorithm library, a private selected definition adds a small
run section: channel, current active range, and `运行此算法`. The UI sends a
run request and polls the normal Run API until it reaches a terminal state.
It shows a compact result card with name, value, unit, quality, channel,
actual range, and a link to the full Results Workbench. No front-end EEG math
is introduced.

A scalar is not shown as a one-point line chart. When both input values are
available and share the same unit, the result card may show a two-bar input
comparison whose X labels are the saved input names and whose Y label is that
persisted unit. Incompatible input units get no comparison chart. Future
multi-window runs may offer only semantically valid axes: time in seconds on X
and the persisted metric unit on Y.
