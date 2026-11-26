# Verification — 2026-11-26

## Fixed behavior
- Partial, empty or duplicate CAR pools never reach ICLabel; valid complete CAR reaches inference with bit-identical fit samples. A non-CAR sample matrix cannot enable classification merely through metadata.
- Infomax uses exact LogRecord arguments, not formatted text. Tests cover 3.30628793175245e-9 rounded to zero at iteration 61 (not converged), true sub-1e-12 convergence, 21 consecutive small-angle steps, restart and missing evidence. The iteration-cap case also uses real MNE.
- ICA review works through the import/HTTP path with no EOG/ECG and frontal proxy disabled. Reference scores remain null. Existing explicit reference validation, regression and SSP tests pass.
- Missing model weights and injected FileNotFoundError produce HTTP 200 with classifier unavailable reason; InvalidProtobuf is handled explicitly. Unexpected scientific ValueError is not swallowed.
- Review/application identities advanced. Restored unmixing is identical; restored mixing and reconstructed samples are checked using rtol=1e-12, atol=1e-15 (V for samples).
- Desktop status explains partial CAR and absent correlation references without claiming fallback scores exist.

## Executed checks
- Backend 17 related modules: **178 passed**, 3 warnings, 61.84 s. Includes classifier, new validation regressions, integration, removal runs/preview, segments, bad channels, reference identity/overlap, validity, consistency, preprocessing, bounded STFT, source cache, recording identities and file-size policy tests.
- Scientific dependency boundary and no direct eeg_core service imports: **2 passed**, 22 deselected.
- Desktop Release review ICLabel/preprocessing selection: **81 passed**, 0 failed. Build reports existing xUnit2013 and CA2022 warnings.
- `uv lock --check`, `uv sync --frozen --extra dev`, and frozen import/version checks pass: MNE 1.13.2, mne-icalabel 0.10.0, onnxruntime 1.32.0. Existing package versions retained; lock also resolves older supported Python environments. Current Python environment was synchronized, not a new clean environment.
- File-size checker passes with registered warnings; classifier 423 lines under reviewed 440 limit.
- `git diff --check` and strict OpenSpec validation pass; Git emits existing LF/CRLF conversion notices.

## Limits / deployment
No full-repository backend suite, packaged application build, real EEG classification accuracy study, hardware run or manual desktop acceptance was performed. Existing unrelated worktree edits were preserved. Solver evidence depends on the locked MNE log structure and fails closed if unrecognized.

Old review/application fingerprints expire because scientific eligibility and recommendations changed; rerun ICA review before applying selections. Recordings are not migrated or modified. No commit or push was requested or performed in this repair turn.

## Review follow-up — 2026-11-26

### Model identity recovery
The exact ONNX path used by the inference package is checked on every metadata request. Successful hashes are bounded-cached by resolved path, file size, mtime_ns and ctime_ns. Missing results are not cached. The isolated-file regression verifies missing → restored → unchanged → same-size modified → deleted → restored, updates model/version evidence and verifies no redundant read for unchanged files. Unrelated backup ONNX files are not accepted as the runtime model. Production model files were not touched. Simultaneous replacement during an active inference is not supported as an atomic model-update operation.

### Independent synthetic preservation checks
`test_ica_clean_signal_preservation.py` runs actual MNE ICA and ONNX classification on three deterministic 30-second, 19-channel, 250 Hz mixtures of four smooth spatial sources (6/10/12/19 Hz), with small sensor noise. No classification is mocked. All three return an empty recommended exclusion set, zero output change, and alpha/beta power ratios of 1.0. Assertions require ready/converged state, no exclusions, relative signal error < 1e-12 and power ratios within 0.95–1.05.

A positive control forcibly excludes the strongest alpha component using the same fitted model. Posterior alpha power falls to 0.0001585–0.0001739 of its prior value (about 0.016%–0.017% retained), below the required 50% threshold. Thus the retention measurement detects real signal loss instead of merely inspecting predicted class labels.

The original interpolation/rank fixture is now named as brain rhythms with independent sensor noise rather than pure clean physiological EEG. Its actual recommendation set is [6, 8, 9, 11, 12, 14, 16, 17] out of 18 components. Applying it produces alpha amplitude ratios 0.991547–1.004375 and beta ratios 0.986032–0.987874; relative whole-signal RMS difference is 0.409583. The test records these values, applies the exact recommendation set and checks rhythm amplitude ratios within 0.9–1.1. Rhythm preservation does not prove that all excluded components were unwanted or that every physiological feature is preserved.

### Final checks for this follow-up
- 18 related backend modules: **182 passed**, 3 warnings, 52.47 s. Includes the three added clean-source cases and the model file-state regression.
- Scientific/service dependency boundaries: **2 passed**, 22 deselected.
- `uv lock --check` and frozen sync dry-run pass; no environment changes required.
- Source file-size policy passes; classifier now 429 lines under its reviewed 440-line allowance.
- Plan body, table and tasks now consistently use fit v5 and application v7 and list all three previously missing unavailable reasons.
- Strict OpenSpec validation and whitespace checks pass.

This follow-up does not modify desktop source and does not rerun the earlier 81 desktop tests. It does not run the full backend suite, packaged deployment or real EEG/UI/hardware acceptance. These synthetic cases are limited regression controls, not population accuracy or false-deletion-rate estimates. No automatic exclusion threshold was changed to obtain these results.

## Broadband fit notch follow-up — 2026-11-26

ICA broadband preparation now applies the selected scientific notch frequency and Q. A disabled notch remains disabled. Review/application metadata and persisted models carry `fit_notch_hz`; the desktop response consumes it and the ready status displays the selected frequency. Fit identity is `ica-fit-v6-notched-broadband-fit`; application identity is `artifact-removal-application-v8-notched-broadband-ica`. Previous v5/v7 statements above describe historical checks. Existing model selections require a fresh review under the new identity.

Executed against this follow-up:
- Backend ICA validation/integration, synthetic preservation, offline removal and file-size policy: **39 passed**, 2 dependency deprecation warnings, 50.66 s. Includes strong synthetic 50 Hz noise with notch enabled/disabled and persisted fit-notch restoration.
- Backend classifier, removal runs/preview APIs, reference overlap, validity and reference-channel suites: **44 passed**, 2 dependency deprecation warnings, 25.95 s.
- Scientific/service dependency boundaries: **2 passed**, 22 deselected, 0.28 s.
- Desktop Release ICA/preprocessing tests: **81 passed**, 0 failed, 0 skipped; build succeeded with existing xUnit2013 and CA2022 warnings.
- Source file-size checker passes with registered review warnings.
- Strict OpenSpec validation and `git diff --check` pass.

No full backend suite, packaged application, new real-recording scientific comparison, manual desktop acceptance or hardware run was performed for this commit. Regression success does not establish optimal ICA fitting or guarantee artifact-free physiological signals. Unrelated UI assets/user-guide edits and private EEG artifacts are outside this commit.
