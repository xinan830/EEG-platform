# PSD Review Panel Design

## Ownership and data flow

`MainWindow` composes a `ReviewAnalysisCoordinator` with the existing generic `ReviewWindowAnalysisAdapter("psd", "PSD")`. Review keeps ownership of the registered recording, playback cursor, request lifecycle, stale-target rejection and subscription cleanup. `ReviewPsdParameters` owns the selected EEG source channel, the current catalog scientific version, the backend-declared dynamic window and step, and PSD's explicit filter defaults. The PSD Review presenter owns PSD array validation, unit projection and status/range presentation. The existing PSD spectrum chart renders those projected points.

```text
Review recording session + one playback cursor
        -> shared ReviewAnalysisBinding / coordinator
        -> generic algorithm adapter -> backend PSD on-demand executor
        -> PSD-owned result validation/projection -> existing PSD spectrum chart
```

## Scientific and identity boundaries

- `algorithm_id=psd`, current catalog version, one available source EEG channel, `mode=dynamic`, and catalog-declared filter defaults/window/step are sent explicitly.
- Filter inputs remain PSD algorithm parameters. The waveform display filter is presentation state and is not copied into the PSD request.
- The registered recording and historical montage snapshot are included using the same fields as other single-channel Review algorithms.
- Backend values, axes, quality, actual window and provenance remain authoritative. UI converts `V^2/Hz` to display `µV²/Hz` only through the existing PSD projection.
- Both Review and standalone PSD presentation consume current `band_share` arrays only; retired `spectral_evidence.relative_band_power` is not interpreted as the current result contract.
- A response is shown only after the shared adapter/coordinator verifies recording/configuration/window identity and the PSD presenter validates its shape and units.

## Rejected alternatives

- Recreating the archived PSD-specific provider would introduce a second PSD execution path and duplicate current shared lifecycle behavior.
- Reusing the entire standalone algorithm workspace would couple Review to unrelated Run/catalog UI and create another owner for configuration state.
- Reimplementing Welch, band integration, filtering, window selection or result quality in WPF would violate the platform scientific boundary.
- Automatically enabling PSD solely because it appears in the catalog is rejected; the current catalog capability and typed result contract are prerequisites.

## Migration, rollback and testing

There is no persisted-state migration. Rollback removes only the new PSD Review registration, its PSD-owned Review presentation/configuration and the new entry points; it does not alter PSD Runs, artifacts, recordings, shared time coordination or the generic adapter. Tests cover parameter/channel/montage serialization, ready and unavailable projection, composition at the cursor captured when opening, channel-change invalidation, and close/replacement cancellation. Manual WPF acceptance must verify a real registered recording, the serialized request, live playback/seek behavior, units/range, and a backend unavailable/rejected state.

## Central integration ledger

| File | Why it changes | Classification |
| --- | --- | --- |
| `MainWindow.xaml.cs` | Registers the PSD algorithm adapter at the existing Review composition root | Registration |
| `RecordingReviewViewModel.Analysis.cs` | Adds PSD to the existing algorithm panel registration map | Registration |
| `RecordingReviewViewModel.cs` | Exposes PSD-owned state, command and panel selection notification | Composition binding |
| `RecordingReviewViewModel.AnalysisBinding.cs` | Publishes the PSD panel selection property through the existing lifecycle | Composition binding |
| `RecordingReviewView.xaml` | Adds PSD menu/toolbar entry and selects its PSD-owned panel template | UI composition |
| `AlgorithmListViewModel.Previews.cs` | Removes the retired PSD band-share result fallback | Current-contract enforcement |

No PSD-specific branch is added to the generic Review coordinator, window scheduler, API client or executor.
