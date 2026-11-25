# Paired scientific preview: three safety fixes (2026-11-25)

Scope: follow-up within `fix-scientific-preview-comparison-baseline`. No algorithm,
filter mathematics, raw recording, database schema or real-time acquisition change.
Backend and desktop must use the same paired-preview response version.

## Fixes and regression coverage

1. Selected montage without a registered backend snapshot is rejected with
   `montage_snapshot_unavailable`, before HTTP preview requests. It cannot silently
   become Raw. Before publication, full displayed channel names must be present in
   the baseline (case-insensitive; no suffix stripping). Ordinary frame rendering,
   sample markers and scientific overlays use the same compatible-comparison
   predicate. Incompatible replies, including a reply replacing a successful pair,
   clear the pair and preserve ordinary colored viewing.
2. An excluded derived output is identified from exact backend
   `processed.excluded_output_channels`, including a bipolar negative/reference
   dependency. Its badge explains the remaining light-gray baseline. Raw electrode
   names are not used to guess confirmed output exclusion.
3. Gap preservation retains the soft target, but paired preview collection has
   finite hard limits: 16000 points per trace, 1000000 scalars across both sides'
   time and amplitude arrays, with nulls counted. Collection checks finite-state
   boundaries before growing index lists and checks each bucket against the
   remaining response budget before serializing values. Excluded baseline-only
   rows count too. Failure is HTTP 422 / `PREPROCESSING_PREVIEW_LIMIT_EXCEEDED`,
   with request_id and an instruction to shorten the viewport. No deletion of gaps
   or zero filling. Desktop transport failure clears a previously successful pair.

New tests: `backend/tests/test_paired_preview_limits.py` (7 cases),
`desktop-client/BrainPlatform.Desktop.Tests/Review/ReviewScientificComparisonSafetyTests.cs`
(4 cases, including success-to-mismatch and success-to-resource-error transitions).

## Automated results

- Backend paired preview, existing preview contracts/follow-ups/API, artifact
  reports/removal/validity, bad channels/segments, scientific layers and bounded
  STFT suites: **99 passed**.
- Scientific layer boundary and no direct eeg_core service import: **2 passed**.
- Desktop filter `ReviewPreprocessing|ReviewArtifactReportConsistency|Scientific_comparison_requires`:
  **49 passed**, Release production and test projects built. The test project
  reports CA2022 and xUnit2013 warnings in existing test files.
- Broader desktop Review suite: **528 passed / 11 failed** (539 total). The same
  failures were observed before these three fixes: six annotation panel width
  expectations (416 versus 378–390), annotation title and event-count expectations,
  analysis status result-publication count, playback restoration after dragging,
  and real HTTP export acceptance without configured inputs (dynamic skip reported
  as failure). They have not been root-caused or established unrelated. This is
  not a green full Review suite.
- Strict OpenSpec validation, executable size policy and `git diff --check` pass.
  Preview service remains 396 lines; canvas 586 lines is explicitly reviewed in
  the existing size-policy entry (cap 590, retain future responsibility split).

Commands used from the repository root (pytest runs with backend as working directory):

```text
backend/.venv/Scripts/python.exe -m pytest tests/test_paired_preview_limits.py tests/test_paired_scientific_preview.py tests/test_preprocessing_preview_contract.py tests/test_preprocessing_preview_followups.py tests/test_offline_artifact_removal_preview_api.py tests/test_artifact_report_consistency.py tests/test_artifact_removal_validity.py tests/test_offline_bad_channels.py tests/test_offline_artifact_segments.py tests/test_offline_artifact_removal.py tests/test_scientific_preprocessing_layers.py tests/test_stft_bounded_preprocessing.py -q
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --artifacts-path .artifacts/review-paired-preview --filter 'FullyQualifiedName~ReviewPreprocessing|FullyQualifiedName~ReviewArtifactReportConsistency|FullyQualifiedName~Scientific_comparison_requires' --verbosity minimal
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --artifacts-path .artifacts/review-paired-preview --no-build --filter 'FullyQualifiedName~BrainPlatform.Desktop.Tests.Review' --verbosity minimal
openspec validate fix-scientific-preview-comparison-baseline --strict
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check
```

## Authorized real-recording numerical check

Private recording and detailed outputs stay outside tracked files. Registered a
149982-sample, 1000 Hz recording with 21 acquisition REF outputs into an isolated
database under gitignored `.artifacts/`, leaving the original recording unchanged.

- Window 38.5–46.5 seconds, soft target 2400: baseline and processed agree exactly
  when enhancements are disabled; repeated cold/hot responses agree.
- Independent SciPy notch filtfilt followed by Butterworth SOS filtfilt over the
  complete recording agrees at emitted original sample coordinates to
  **0.00004999991 µV**, within four-decimal µV serialization rounding.
- Excluding C4 retains 21 baseline rows and returns 20 processed rows with exact
  excluded output `C4-REF`.
- Bad-segment rejection request succeeds within budget (73348 response scalars).
  This viewport did not establish all real gap/ICA/interpolation behavior; those
  broader acceptance items remain open.
- Observed cold/hot request durations: 0.433 / 0.070 seconds on this workstation
  for this run, not a performance guarantee.
- SHA-256 of every source-directory file agrees before and after the run.

Not performed: full backend directory regression, interactive WPF acceptance,
hardware acceptance, real-recording ICA/interpolation graphical acceptance.
No commit or push was requested for this fix.
