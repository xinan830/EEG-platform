# Design

## Scope and result semantics

This change runs a saved, user-private scalar definition against one recording,
one existing channel, one committed time range, and an immutable definition
ID/version. The
server resolves only the curated feature identifiers emitted by the user
builder: absolute Delta/Theta/Alpha/Beta power (`uV^2`) and relative band power
(`ratio`). It uses the existing offline-spectral-v3 preprocessing and Welch
calculation, never Viewer settings.

Static mode executes once over an explicitly selected start/end range. Dynamic
mode is a persistent playback-synchronised session: at each whole playback
second `t >= selected_window_s`, the browser requests the real trailing
`t-selected_window_s → t` EEG window. For the default `10 s` selection, at
`46.x s` it requests `36–46 s`, then at `47.x s` requests `37–47 s`. The
browser appends backend-returned points but performs no EEG or metric
calculation. A quality failure retains a point with `value: null`; it is never
removed or converted to zero.

If a researcher enables dynamic mode in the middle of playback, the browser
first requests a bounded real-history range:
`max(0, t-(selected_window_s+20)) → t`. The backend
therefore returns the corresponding one-second dynamic points immediately
(for example, enabling at `25 s` returns `10…25 s` endpoints), rather than a
misleading one-point chart. This is a display bootstrap, not interpolation;
advances while a Run is pending, the next request covers every missed
one-second endpoint before ordinary one-second appends resume.
every point remains a backend-computed selected-duration trailing window. If
playback advances while a Run is pending, the next request covers every missed
one-second endpoint before ordinary one-second appends resume.
advances while a Run is pending, the next request covers every missed
one-second endpoint before ordinary one-second appends resume.
While an appended Run is queued or running and therefore has no metric payload,
the client retains the currently rendered backend history. Only a returned
dynamic payload may replace a same-endpoint point or append a new endpoint;
the client must never clear historical points merely because a pending Run has
`result: null`.

The dynamic-window selector is shared with the time-frequency workflow and
allows `5`, `10`, `20`, or `30` seconds, with a fixed one-second step. The
chosen duration is part of the Run configuration, cache identity, provenance,
returned dynamic contract, and visible chart label. Ten seconds remains the
default; five seconds is allowed as a fast but higher-variance view because it
contains fewer 4-second Welch segments.

The displayed trend viewport is the selected duration ending at its newest
returned time point: a `10 s` selection at endpoint `32 s` displays `22–32 s`,
not an arbitrary narrow span based on the count of received points. Changing
the selector while a dynamic session is active invalidates its prior results
and starts a new bounded real-history bootstrap with the selected duration.

The resolver supplies backend `Scalar` values to the existing closed graph
executor. The output must retain the executor's unit and quality state. A
quality-gated spectrum produces `gate_failed` with null numerical output; it
is never changed to zero.

## Persistence and API

`RunCreateRequest.analysis_type` gains `definition_metric`. Its config has
`channel` and absolute `time.start_s/end_s`. A run must identify both a saved
definition and an existing immutable version. Static requests use user-entered
times; dynamic playback requests have an exact selected-duration range. The resolved definition version,
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

## Replay reset, pending view and algorithm evidence

Replay is a new playback epoch, not a continuation of an existing dynamic
analysis session. On replay, the browser clears rendered dynamic result points
and resets the last requested dynamic endpoint to `null`, while retaining the
user's selected definitions, channel and selected dynamic duration. The panel
is immediately visible at `0 s`; it does not submit a Run until a complete
selected-duration trailing EEG window exists. Consequently, a `10 s` session
shows a truthful pending state for `0–10 s`, produces its first real point at
`10 s`, and never carries a `22 s` point into the new playback epoch.

The pending chart has an empty data series and labels its future first window;
it MUST NOT create a zero, interpolated value, or a fake line. Its axis is a
display guide only and its text distinguishes “waiting for a complete EEG
window” from a rejected quality-gated window.

Every static result card and dynamic trend exposes an **algorithm debug
workbench** button. It is read-only. A static view shows the completed Run's
identity/version/configuration, requested and actual range, channel, analysis
reference, sampling rate, filtering and Welch contract, spectral quality,
resolved input features, algorithm output and trace identity. A dynamic view
selects one persisted endpoint (latest by default) and shows the corresponding
real trailing window and evidence. The server persists the relevant raw
spectral evidence for each metric computation: frequency axis, selected-channel
linear PSD, absolute/relative band power, contracts, quality and resolved
inputs. The client renders these values and may expand the 117 PSD points; it
does not recompute PSD, band integration, RBP or the user formula.
