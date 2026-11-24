# Add PSD to Recording Review

## Why

PSD already has an official backend algorithm and declared on-demand windows, but the recording-review workspace does not register a PSD panel. Adding it through the current shared Review path makes PSD available beside the existing Review algorithms without duplicating recording access, playback time, scheduling, or science.

## What Changes

- Register PSD through the current `ReviewWindowAnalysisAdapter`, shared recording registration, `ReviewAnalysisBinding`, and backend-planned on-demand execution.
- Add PSD-owned channel/configuration and result presentation, reusing the existing PSD window validator/projection and spectrum chart.
- Remove the retired PSD band-share fallback so PSD presentation consumes only the current structured result contract.
- Expose the panel from the recording-review algorithm menu and toolbar.
- Add focused parameter, presentation, and lifecycle tests.

## Compatibility Impact

No retired PSD Review provider, saved-result reader, or fallback execution route is restored. Review and the existing PSD workbench consume the current PSD result contract only; historical artifacts remain untouched, but retired `spectral_evidence.relative_band_power` values are not converted into current band-share output.

## Scientific Contract Impact

No formula, filter, frequency-axis, window policy, unit, quality gate, or preprocessing behavior changes. The backend remains the only scientific authority. Review sends an explicit single source channel, dynamic mode, catalog-declared window/step, current scientific version, and recording/montage identity. Display montage and waveform display filters are not scientific PSD inputs.

## Non-Goals

- No backend formula/API execution changes or new scientific version. The catalog will expose the existing PSD configuration defaults explicitly so Review does not duplicate them.
- No independent playback clock, Review recording reader, full Run per cursor, or frontend PSD calculation.
- No restoration of the archived PSD-specific Review provider or its saved-window path.
- No PSD redesign beyond fitting the existing spectrum chart into the Review panel shell.

## Migration

No data migration is needed. The PSD Review panel is newly registered through the current shared on-demand path; closing it releases its foreground subscription while leaving immutable backend results and waveform playback intact. The algorithm catalog adds explicit values matching the current PSD configuration defaults; submitted calculations do not change.
