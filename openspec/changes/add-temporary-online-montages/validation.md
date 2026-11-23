# Validation — 2026-11-23

## Scope and contracts

The earlier single appended trace implementation has been replaced by a complete session-only montage profile. In acquisition and review, 在线导联 opens the existing full 导联设置 view, with configuration name, references, positive/negative sources, colors and output order. Sources are fixed to the current session. Save validates the profile, adds it to the display-montage dropdown and automatically selects the entire configuration. Cancelling preserves the current display; an ordinary profile can be selected and the temporary profile selected again.

Temporary selection changes only waveform projection. Tests preserve original raw arrays, recording manifest channels, V units, sample-counter/time coordinates, algorithm montage snapshots and review algorithm selection. Review algorithm registration uses ordinary montage candidates, excluding the temporary UI entry. No backend, API/WebSocket or scientific formula changes are involved.

The editor uses return-only session saving without montage-store writes or device refresh. Acquisition stop/new session and review disposal clear temporary state. Complete profile fingerprints include references, output order and colors. Review invalidates projected frames, fences superseded foreground/prefetch requests, retains source/filter caches and bypasses persistent display-level publication for temporary profiles. Acquisition render identity also includes the complete profile and visible outputs.

## Focused desktop verification

Command (PowerShell):

    dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~WaveformDisplayFrameBuilderTests|FullyQualifiedName~WaveformRenderRevisionTests|FullyQualifiedName~MontageDisplayProjectorTests|FullyQualifiedName~TemporaryMontageEditor|FullyQualifiedName~RecordingReviewSessionTests|FullyQualifiedName~RecordingReviewWindowCacheTests|FullyQualifiedName~LiveAcquisitionMappingTests|FullyQualifiedName~OnlineProfileAutoSelects" -p:BaseOutputPath=.isolated-build/online-profile-20261123/ -p:UseAppHost=false --logger "trx;LogFileName=online-profile-focused-final.trx" --results-directory artifacts/online-montage-20261123/results -v minimal

Result: **48 passed, 0 failed, 0 skipped**. The preceding build compiled desktop C#/WPF/BAML successfully in the same isolated output. Coverage includes complete editor bindings and draft validation, AVG/reference settings, colors/order, full waveform replacement, dropdown auto-selection/switch-back, acquisition preview/recording isolation, unchanged writer metadata, review algorithm selection, cache identities, superseded requests and lifecycle cleanup.

Evidence: artifacts/online-montage-20261123/results/online-profile-focused-final.trx.

## Extended regression and baseline

The extended selection of montage, acquisition-mapping, waveform, review-session/cache and algorithm-selection tests completed with **192 total: 191 passed, 1 failed**. This is not an all-green regression run.

The single failure is MontageReferenceReleaseTests.EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest. The unchanged historical-reference reader rejects an empty registered identity set that conflicts with the recording manifest. The same test failed in a clean copy of committed HEAD (759b90e), confirming an existing baseline failure. Its data-protection behavior was not changed as part of temporary waveform configuration.

Evidence: artifacts/online-montage-20261123/results/online-profile-regression.trx; isolated baseline checkout: artifacts/online-montage-20261123/baseline-759b90e.

## UI and static verification

- Constructed and rendered the real full editor in a WPF test, then inspected artifacts/online-montage-20261123/online-montage-full-editor.png. It shows the named session configuration, fixed source selector, reference controls, complete output table, colors/order and save/return actions. This was offscreen rendering; no desktop controls were operated.
- git diff --check: passed; an existing LF/CRLF advisory concerns the separately edited LiveSpectralAnalysisViewModel.cs.
- python backend/scripts/check_file_sizes.py: passed for its configured scope. Changed desktop C#/XAML files were also compared with docs/code-size-policy.json; over-limit existing files retain registered responsibility assessments and split plans.
- openspec validate add-temporary-online-montages --strict: passed.
- No obsolete single-trace type or simplified dialog references remain in the acquisition, review or montage implementation.

## Limitations and preserved work

No real-device recording or interactive acquisition/review UI acceptance was performed. The running desktop process was not stopped, restarted or replaced; rebuild/relaunch is required to load this implementation. Offscreen editor rendering and automated tests do not establish hardware acceptance.

No new dependency, automatic Git commit/push, change archival or unrelated-file cleanup was performed. Concurrent storage-status work, live spectral changes and other working-tree edits remain preserved.
