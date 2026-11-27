# Verification — 2026-11-27

## Defect and root cause
Local Windows/.NET 10 Release tests; no EEG input file or backend request is needed. At 500 Hz, retain ten seconds of preview, enter Recording before its first accepted batch, then pause while preview continues. Expected recording duration: zero. Previously both UI entry points returned ten seconds because `GetEffectiveAcquisitionDuration` substituted the preview origin for a missing recording origin. Pause bookkeeping could also retain preview-only state before the first accepted recording sample.

## Fix and scope
- `LiveDisplayTimelineTracker.Update` receives an explicit recording origin; before it exists, only the continuous display origin is updated.
- `GetEffectiveAcquisitionDuration` returns zero until the recording origin exists; no preview-origin substitution remains.
- `LiveMonitoringViewModel.Refresh` and `GetWaveformSource` each read the recording-origin provider once and reuse the value for update/calculation.
- Existing lifecycle-boundary accounting remains first choice once the recording origin exists. No raw-writer, acquisition-coordinator, API/WebSocket, units, channel-order or filter changes were made.
- The older pause fixture now supplies its actual recorded origin, zero, rather than implicitly using preview.

## Red/green evidence
The four parameterized `RecordingBeforeFirstBatch_DoesNotCountPreviewOrInitialPause` cases were run before production edits: all four failed, expected `00:00:00`, actual `00:00:10`. Cases cover device counters beginning at zero or 10,000, with and without lifecycle boundaries.

After the fix, these cases cover both monitor entry points through Recording → Paused → continuous preview → Resumed (no recorded sample yet) → first recorded second. The existing lifecycle test additionally covers a second pause and a resume before the next recording batch. Display origins remain unchanged and display-counter adjustments remain empty.

## Commands and results
- Focused Release suite: `dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~LiveWaveformOverlayAndTimelineTests|FullyQualifiedName~LiveMonitoringViewModelTests|FullyQualifiedName~AcquisitionCoordinatorTests' --logger 'console;verbosity=minimal'`: **29 passed, 0 failed**, 505 ms.
- Related Release suite using `--no-build --no-restore --filter 'FullyQualifiedName~Channels|FullyQualifiedName~Montage|FullyQualifiedName~Electrode|FullyQualifiedName~Acquisition'`: **525 passed, 0 failed, 0 skipped**, 10 s.
- Full desktop Release suite, no filter/exclusions: **1814 passed, 2 failed, 1 skipped**, 57 s. TRX: `.artifacts/recording-duration-origin/recording-duration-origin-full.trx`. The acceptance export test skips because its external acceptance recording is not configured.
- Isolated rerun of the two full-suite failures: **1 passed, 1 failed**, 108 ms. `ScreenCalibrationTests.ViewModel_ExplicitSaveRestoresInputsInANewInstance` passes in isolation; the full-suite stack reaches static calibration notification → review canvas → a different thread's Window handle. `CacheMaintenanceTests.LinkedDirectoryRefusesBothLocalTreesBeforeDeletingFiles` fails again at `Directory.CreateSymbolicLink` with insufficient OS privilege, before the cache-cleaning assertion. These code paths and tests were not modified in this fix. No claim is made that desktop full regression is green.
- Backend gate: `backend/.venv/Scripts/python.exe -m pytest -c backend/pyproject.toml backend/tests -m gate -q`: **60 passed, 1827 deselected, 2 deprecation warnings**, 8.12 s.
- `backend/scripts/check_file_sizes.py`, `git diff --check -- backend docs desktop-client`, and `openspec validate fix-recording-duration-origin --strict`: exit 0. Production tracker: 251 lines; monitor: 386 lines; timeline tests: 267 lines; monitor tests: 375 lines. No new size exceptions.

## Limits
No real-hardware/manual UI acceptance or full backend regression was performed. This fix does not resolve the separate calibration-event threading problem or change Windows symbolic-link privileges. No commit/push was requested or performed.
