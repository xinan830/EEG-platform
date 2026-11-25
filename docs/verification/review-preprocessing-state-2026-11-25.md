# Review preprocessing state and display verification

Date: 2026-11-25 (Asia/Shanghai). Change: `fix-review-preprocessing-state`.
Checkpoint before implementation: `eb863645bf84d7fa44c9b2c15219b9388108f955`, pushed to `origin/main`.

## Scope and invariants

This change fixes the seven reproduced Review preprocessing state/display findings.
Scientific algorithm modules, filter coefficients, ICA fitting/removal formulas,
original recordings and persisted source samples are unchanged. Backend arrays
remain sample-major float64 volts; preview coordinates are absolute seconds and
display amplitudes are microvolts. Local display filters stay independent.

## Reproduction and repairs

1. **Selected treatment reported as completed:** selecting interpolation/exclusion
   previously recolored original traces and claimed repair. Without a matching
   successful preview, badges now say the strategy awaits preview verification;
   original traces retain their original appearance. Processed styling uses returned
   treatment metadata only.
2. **Cancelled ICA review changed committed model:** pending reports/cards are now
   separate from the confirmed fingerprint/component tuple. Confirmation validates
   input revision, applicability, fingerprint and component membership atomically.
   Cancelling the dialog preserves committed configuration.
3. **Incomplete invalidation:** scientific-input changes and bad-channel rescans
   invalidate ICA state centrally. Shared montage-selection events enter this same
   path. Inspections use operation generations, configuration revisions and linked
   cancellation; reset/disposal prevents late publication, even if transport ignores
   cancellation.
4. **Stale preview on errors/reset/playback:** a replacement clears old evidence
   before loading. Preview publication checks generation, configuration, viewport,
   recording, montage and returned time range. Playback suppresses static previews;
   pause refreshes them. Late errors do not replace newer/reset status.
5. **Uniform preview point dropping hid gaps/peaks:** per-channel finite-run
   first/min/max/last envelopes preserve extrema and explicit null gaps. Mandatory
   boundaries may exceed the point target; this is declared in metadata. Scientific
   arrays are not resampled.
6. **False bipolar ICA scalp positions:** formula provenance controls eligibility;
   raw, hardware REF and mean-reference outputs retain supported sites, while
   channel-difference/unsupported derived outputs receive a structured exclusion
   reason. Without formulas, only raw/REF/AVG labels are accepted.
7. **Partial scan called whole-recording clean:** summaries show actual response
   start/end, including a resource-limited range shorter than the requested first
   180 seconds. A clean conclusion applies only to that scanned range.

The panel also visibly states that bounded STFT/TNK do not apply ICA/SSP/EOG
removal. Preview status identifies its independent 1–50 Hz plus 50 Hz notch
scientific filter and display-envelope compression.

## API/consumer synchronization

`POST /api/recordings/{recording_id}/preprocessing/preview-window` replaces shared
`times_s` with `trace_times_s: {channel: absolute_seconds[]}` and adds typed
`display_compression` metadata. Every trace has its own equally sized time array.
Backend response schema, desktop DTO, validation, renderer and fixtures are updated
together. Deploy/restart backend and desktop together; the desktop rejects obsolete
compression/time/channel contracts instead of silently drawing them.

## Automated evidence

- Initial desktop regressions: **8 failed / 3 passed**, reproducing premature
  treatment claims, cancelled ICA replacement, invalid confirmation, rescan
  invalidation, reset races, stale preview retention and missing scan coverage.
- Initial backend display regressions: **4 failed / 3 passed**, reproducing lost
  narrow gaps, missing independent coordinates and incorrect bipolar scalp placement.
- Final focused desktop suite: **54 passed / 0 failed**, including 21 preprocessing
  state/contract tests plus montage/import/projector integration.
- Backend focused suite: **104 passed / 0 failed** across preview envelopes, the
  previous five preprocessing bug fixes, bad channels, bad segments, artifact
  removal/scientific/API paths, continuous/bounded caches, recording access/service
  and official algorithm runs.
- After adding the typed API response, preview/API tests plus two scientific
  architecture-boundary tests: **13 passed / 0 failed**. These overlap the focused
  suite and must not be added to its count.
- Desktop Release build: **0 warnings / 0 errors**. Test-project compilation retains
  two existing analyzer warnings in unrelated tests.
- OpenSpec strict validation, `git diff --check` and modified-source size assessment
  pass. New/expanded preprocessing owners stay below 400 lines. Existing main
  ViewModel/canvas remain within their registered size limits.
- The former 600+ line artifact-removal test module is split by responsibility into
  scientific tests, preview API integration and official-run API integration; all
  original six tests remain collected.

Backend command (from `backend`, with `.venv/Scripts/python.exe -m pytest -q`):
`test_preprocessing_preview_contract.py`, `test_offline_preprocessing_bugfixes.py`,
`test_offline_bad_channels.py`, `test_offline_artifact_segments.py`,
`test_offline_artifact_removal.py`, `test_offline_artifact_removal_preview_api.py`,
`test_offline_artifact_removal_runs_api.py`, `test_scientific_preprocessing_layers.py`,
`test_stft_bounded_preprocessing.py`, `test_recording_access.py`,
`test_recording_service.py`, `test_official_algorithm_runs.py` under `tests/`.

## Broader failures and limits

The entire architecture test module reports **21 passed / 3 failed**. Failures match
the previously recorded brainbeat window maximum, custom-frequency private
scientific import and live-RBP concrete-science import. Those files are unchanged.

The broader desktop `FullyQualifiedName~Review` run reports **582 passed / 13 failed**
(before the final two new disposal regressions). Eleven match earlier findings:
annotation width (six cases), annotation title, annotation spacing, analysis-status
notification, drag playback state and an unconfigured real-HTTP acceptance test
reported as a dynamic-skip failure. Two additional RBP chart tests expect five
series but observe zero; both also fail when run in isolation. Their source and
chart implementation are unchanged here; they require separate investigation and
are not claimed resolved or conclusively attributed to an earlier commit.

No full backend-directory run, actual desktop interaction/screenshot acceptance,
hardware validation or new real-patient EEG effectiveness assessment was performed.
These automated results establish the stated engineering contracts, not clinical
validation or a claim that the entire repository is regression-free.

## Follow-up verification: four review findings

Scope: unchanged artifact/bad-channel rescans, temporary waveform montages,
derived interpolation metadata and ICA miniature waveform envelopes. No recording
data, scientific filter, bad-segment detector, ICA fit/removal formula or algorithm
configuration was modified by this follow-up.

Root causes and corrections:

1. Inspection completion previously invalidated ICA and the scientific preview
   unconditionally. Reports now update independently; actual selected bad-channel
   changes or automatic none-to-reject transitions invalidate scientific state.
   Existing manual selections and explicitly disabled bad-channel policies survive
   rescans. Pending and confirmed ICA survive unchanged scans; an untouched ICA
   section retains its initial status.
2. Temporary waveform selections bypass the analysis montage event. The existing
   session-change integration now derives preview compatibility from both the
   requested temporary profile and the currently displayed frame. It suspends
   before asynchronous loading and resumes only after an ordinary frame returns;
   stale preview responses are fenced and playback remains independently paused.
   Both create/replace and dropdown reselect paths use this same state boundary.
3. Interpolated outputs were inferred from the label prefix, missing negative and
   CAR reference dependencies. Preview metadata now follows provenance formulas,
   and desktop badges consume verified output metadata before source-name matching.
   Tests cover bipolar positive/negative inputs, CAR pools, case-insensitive raw
   channels and rejection of unsupported prefix guessing.
4. ICA previews sampled uniform indices after deleting missing rows and normalized
   only the sampled points. Shared extrema index selection now preserves full
   snippet extrema and gaps. Finite pointwise MNE projections are scattered back
   to original sample positions; normalization uses all finite snippet activations.
   The API returns null gaps, explicit recording seconds, full snippet duration,
   z-score units and compression metadata. WPF validates and renders those times,
   including trailing unavailable spans, rather than uniformly spacing points.

Reproduction before fixes: the first five backend cases produced **4 failed /
1 passed** (bipolar/CAR metadata, false prefix matching, lost 20 ms pulse); the
first five desktop cases produced **4 failed / 1 passed** (ICA invalidation,
initial status and overwritten manual selection).

Final verification:

- Backend focused suite: **115 passed / 0 failed** (the previous 12 modules,
  `test_preprocessing_preview_followups.py` and `test_file_size_policy.py`).
- Two scientific architecture boundary checks: **2 passed / 0 failed**; the
  unrelated architecture failures documented above were not rerun or resolved.
- Desktop focused suite: **67 passed / 0 failed** across preprocessing state,
  ICA JSON and WPF rendering, temporary montage transitions, import/montage/projector
  and imported-recording algorithm registration. Includes a pending frame load,
  transport-ignored cancellation, playback/temporary-montage interaction, pending
  and confirmed ICA preservation and actual drawn X coordinates/gaps.
- Release application build: **0 warnings / 0 errors**. Two existing analyzer
  warnings remain in unrelated test files; no new analyzer warnings remain.
- OpenSpec strict validation and standard `git diff --check`: passed.
- All 19 modified/new production sources assessed against the size policy passed.
  The main Review VM (534 lines) and canvas (578 lines) remain within their
  registered limits; other changed production files remain below 400 lines.

Backend and desktop must be updated together: ICA preview nonempty waveforms
without the new coordinate/unit contract are rejected rather than misrendered.
These are engineering regressions and an automated WPF drawing check, not a new
manual UI/hardware or real-patient scientific-effectiveness acceptance. No full
repository regression was rerun. Changes remain local; this follow-up did not
commit or push source or generated validation artifacts.
