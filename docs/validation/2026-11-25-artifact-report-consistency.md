# Offline artifact report consistency verification — 2026-11-25

## Defect and root cause

The Review artifact list could show channel-specific PTP intervals absent from the scientific preview mask. Inspection independently filtered a bounded crop with high-pass → notch → low-pass (default 0.5–45 Hz); execution used continuous notch → bandpass (Review 1–50 Hz). Bounded algorithms also refitted detection on their selected channel/time crop. Threshold equality did not imply equal detection inputs or results.

## Implemented boundary

- The existing annotated continuous prepared source owns the immutable pre-removal artifact report and source identity. Reject/removal preparation copies that source and applies its exact intervals without filtering or detecting again. The existing single-flight cache is shared; no parallel detector cache is introduced.
- Inspection returns a range-clipped view of this report, including scientific filter/Q values and full-recording detection scope/identity. Defaults match Review scientific preview, 1–50 Hz with 50 Hz notch Q=30. Requested coverage still limits returned intervals; it no longer alters detector fitting context.
- STFT/TNK retain their finite-context signal filters. With artifact QC enabled, their quality gate projects the same complete-channel continuous report. This requires explicit recording context, validates source/montage identity, preserves per-channel half-open sample intervals and rejects missing/mismatched context. QC-disabled bounded reads retain their existing behavior.
- Detection version participates in prepared-source, ICA-confirmation and full/on-demand algorithm identities. Existing historical artifacts are not rewritten.
- Detector input changes clear stale desktop rows/markers. Unchanged rescans and policy/removal changes retain valid detections. The UI distinguishes full detection scope from the currently listed range.

## Automated verification

The new regression file first failed on scan/mask differences, duplicate detection and unsupported bounded recording context. It now covers exact masks; annotate/reject single detection and concurrent coalescing; range clipping and channel projection; parameter/identity isolation; preview null gaps; explicit invalid input failures; one preparation-complete notification; and resource-budget rejection rather than changing detection channels.

Related desktop tests: 61 passed, including six new stale-report/policy tests and existing unchanged-rescan, ICA confirmation, preview races, montage and panel lifecycle cases. Build used a separate ignored output directory because the running desktop executable was locked; the running client was not terminated.

Final combined backend run: 235 passed, 3 failed, across 22 modules (36.76 s); the 11 new defect regressions all pass. Coverage includes official/full/on-demand algorithms, bad channels, artifact rejection/removal, continuous/bounded preparation, numerical filtering, caches, resource budgets, preview and architecture checks. The three pre-existing architecture failures concern unchanged files: Brainbeat dynamic maximum metadata; custom_frequency_inspection importing a concrete primitive; live_rbp_algorithm importing algorithm science. These failures were reproduced before the final verification and remain outside this defect's scope.

File-size policy, whitespace and strict change validation pass. Changed business files remain below the 400-line target.

## Real-recording evidence

Read-only verification used the previously supplied recording: 149982 samples, 1000 Hz, 21 enabled EEG outputs, acquisition REF montage. Raw source file hashes were identical before and after; original files were not modified. All patient-specific reports remain under ignored `.artifacts/artifact-report-fix/`, not this document or tracked fixtures.

- Inspection intervals exactly equal continuous applied intervals. An independently assembled per-channel NaN mask equals the prepared matrix mask sample for sample.
- Annotate and reject share the same immutable report; unmasked sample values are exactly unchanged.
- Bounded quality evidence identifies the same report as inspection. Scientific preview emits null gaps across the detected EMG interval.
- At 1–50 Hz/Q=30 the authoritative report contains initial PTP rejection at 0–0.600 s for two channels and EMG at 138.069–138.384 s for all 21 channels. The independently filtered old scan's tail entries are absent from both current list and execution. This change establishes consistency; it does not claim the detector identifies every physiological artifact.
- Clean-region PSD/STFT requests succeed. EMG-region PSD rejects non-finite prepared samples; STFT rejects the same report interval. Both tail-region requests can still reject via their unchanged amplitude quality gate; this is distinct from the configurable artifact interval report.

## Limits and deployment

No full repository test run, interactive desktop visual acceptance, live hardware test or clinical validation was performed. Enabling artifact QC now requires a cold full-recording preparation even for bounded algorithms; subsequent matching requests reuse it. Existing preparation disk budgets fail explicitly rather than silently reducing the detection population. Deploy/restart backend and rebuilt desktop together, reopen the recording and rescan. No schema migration or raw-data conversion is needed. Rollback producer, consumer and identity changes together.
