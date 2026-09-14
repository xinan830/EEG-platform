# Design

## Scope and result semantics

This change runs a saved, user-private scalar definition against one recording,
one existing channel, one committed time range, and an immutable definition
ID/version. The
server resolves only the curated feature identifiers emitted by the user
builder: absolute Delta/Theta/Alpha/Beta power (`uV^2`) and relative band power
(`ratio`). It uses the existing offline-spectral-v3 preprocessing and Welch
calculation, never Viewer settings.

Static mode executes once over the committed range. Dynamic mode requires an
outer range of at least 10 seconds and evaluates real trailing 10-second EEG
windows at 1-second ends: `10-40 s` returns windows `10-20` through `30-40`,
with X values `20` through `40` seconds. A quality failure retains a point with
`value: null`; it is never removed or converted to zero. A run with no clean
point becomes `gate_failed`.

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

The algorithm library explains, versions and deletes definitions. It does not
run EEG algorithms. A separate waveform-and-algorithms workspace makes
waveform context primary, lists eligible saved definitions as checkboxes, and
offers static/dynamic controls. It sends normal Run API requests and polls
each run independently. Static results are value cards; dynamic results are
backend-derived trend charts. No front-end EEG math is introduced.

A scalar is not shown as a one-point line chart. When both input values are
available and share the same unit, the result card may show a two-bar input
comparison whose X labels are the saved input names and whose Y label is that
persisted unit. Incompatible input units get no comparison chart. Trend axes
are fixed: window-end time in seconds on X and the persisted metric unit on Y.
Algorithms with different output units render in separate charts.
