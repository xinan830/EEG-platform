# Design

Each runnable algorithm supplies an `IAlgorithmRunResultHandler`. Despite its
name, this adapter covers both algorithm-specific input parsing and completion
integration. The registry contains constructor registrations only. Unsupported
algorithms fail explicitly instead of receiving a guessed preview type.

`AlgorithmRunInputs` is a typed input snapshot: recording-relative seconds,
raw channel names, sampling rate in Hz, and UI parameter text.
`AlgorithmRunParameters` supplies the existing request serializer's fields.
The backend remains the scientific authority. No EEG calculations move to WPF.

The common Run flow validates recording/channel/time and declared dynamic
policy, resolves one adapter, submits the request, and invokes that same
adapter on completion. PSD/STFT load structured artifacts. Scalar algorithms
use backend summaries. IAPF, Peak Frequency and RBP parse their own dynamic
windows and send `DynamicWindowRow` values to a generic timeline integration
method. Rejected values remain unavailable.

## Behavior Preservation

- PSD/STFT: requested frequency bounds and disabled/50/60 Hz notch values stay
  identical in static and dynamic requests.
- FAA: F4 must exist in the registered recording and differ from F3.
- STFT: the existing four-second minimum range remains enforced.
- Band Ratio: numerator and denominator roles stay distinct.
- IAPF/Peak Frequency: restore the prior cursor after dynamic reruns.
- RBP: retain its existing first-available-window initialization and versioned
  four/five-band descriptions. Cursor restoration is not broadened here.
- Shared quality card and charts retain their existing data and XAML layout.

## Remaining Boundaries

This is one completed reduction of central coupling, not full module isolation.
PSD/STFT preview matrices and projection state are owned by
`PsdPreviewState` and `StftPreviewState` in their algorithm modules. The
catalog retains only loading, timeline coordination, and compatibility
forwarders for existing shared bindings. The shared parameter card consumes
`AnalysisContext.Parameters` instead of binding directly to catalog fields,
but that context currently forwards the existing catalog-owned values. Preset
collections, detail composition and algorithm-specific parameter schemas still
require explicit integration. A new algorithm with new parameter types or an
artifact shape will require additional integration work.

For an algorithm using existing scalar parameters/results, add its adapter in
its own directory and one registry entry; Run orchestration does not need a
new ID branch. Detail registration and view wiring remain explicit.

The catalog main file shrinks, but remains above 400 lines. Its existing split
assessment in `docs/code-size-policy.json` remains applicable; remaining
responsibilities are selection, automatic execution and shared state.

## Verification And Rollback

Compile the current application and full desktop test assembly into isolated
output folders because the user's application is running. Validate parameter
boundaries, result windows, cursor behavior and the quality card. A WPF
visibility test must process queued binding transfers before asserting its
visual state; direct source-state assertions are retained.

Rollback consists of reverting these adapter/orchestration edits together.
No data migration is necessary. Do not revert parallel RBP/IAPF work.
