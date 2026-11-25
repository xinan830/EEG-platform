# Offline artifact-removal gap provenance acceptance — 2026-11-25

## Defect and scope
An ICA/SSP spatial transform requires complete input rows. Previously a single-channel QC gap made additional outputs NaN without identifying that reconstruction dependency. The authoritative detector report correctly retained its original per-channel intervals, which made the waveform appear inconsistent with the report.

The shared scientific result now records exact half-open intervals with missing input channels, unavailable output channels and additionally unavailable output channels. ICA, SSP and EOG regression use the same accounting. Preview API and desktop status distinguish reconstruction unavailability from newly detected artifacts. PSD/RBP spectral success/failure evidence and the FAA paired-signal source project the same report to requested absolute windows and output channels, retaining missing-input names outside that output selection.

No ICA formula, fitting strategy, component recommendation, detector thresholds, raw files, units, reference or channel order changes. No zero-filling or raw-as-cleaned fallback. Empty exclusion preserves samples and masks exactly. Active application evidence/cache versions change; reviewed fitting identities do not.

## Automated checks
- Related backend modules and two scientific-boundary checks: **177 passed**, three external-library warnings.
- Desktop review preprocessing tests and WPF compilation in isolated output directory: **42 passed**. Initial normal Debug build was locked by the running desktop executable; isolated build succeeded without stopping that process.
- Scope includes ICA independent-MNE equality, no-op, NaN/Inf masks, overlapping channel gaps, clipped time/channel projection, SSP/regression accounting, algorithm identity version versus stable fit identity, typed preview API, spectral failed gates, FAA provenance and desktop invalid-response/status checks.
- File-size policy, strict OpenSpec change validation and `git diff --check` pass. Removal module 421 lines (registered maximum 430), spectral service 494 (550), recording algorithm context 434 (440); dedicated validity modules remain under 400.

## Real recording acceptance
Private recording and reports remain under ignored local storage; no patient names, paths, raw samples or identifiers are included here.

- 149,982 samples, 1,000 Hz, 21 EEG outputs; 1–50 Hz and 50 Hz notch; unchanged QC parameters.
- Original pre-ICA matrix is exactly equal to the previous private acceptance matrix, including missing values. Original three detector intervals remain unchanged.
- Both previously fitted 15-component and 20-component models were reused; only reviewed component index 3 was removed. This check validates application, not a new fit or clinical component diagnosis.
- Output finite samples equal independent public MNE application with **maximum absolute error 0 V**. Every missing output sample and all **27,089 additionally unavailable channel-samples** are exactly accounted for by returned validity intervals.
- The final local artifact remains scoped to one detected channel; its 20 additionally unavailable reconstructed outputs are separately reported as missing-input dependencies. The detector report is not broadened.
- Preview nulls and absolute times, clean-window spectral success, overlapping-window quality rejection, FAA paired-source provenance and prepared-cache reuse agree.
- All original file hashes are unchanged.

## Limits
This is engineering/numerical acceptance, not clinical validation. Necessary multichannel reconstruction gaps remain unavailable; the repair makes their cause and scope explicit. No live hardware or manual screen-layout acceptance was performed, and no Git commit/push was requested. The complete architecture module still reports 21 passed / 3 failed on existing unrelated issues: `brainbeat` dynamic window metadata, `custom_frequency_inspection.py` concrete facade import and `live_rbp_algorithm.py` algorithm implementation import. These files were not changed by this repair.
