# Exact rolling amplitude detection verification — 2026-11-25

## Root cause and implementation

The shared amplitude detector checked a half-second window every 125 ms. Neighboring tested windows could each remain below the threshold while an intervening, untested window exceeded it. This was an execution defect, independent of the earlier inspection/execution report mismatch and independent of the misleading full-height channel-specific UI bands.

The shared detector now evaluates every complete sample-aligned window using the installed SciPy maximum/minimum filters. It uses per-channel working arrays rather than a samples-by-window-width materialization. Threshold comparison remains strict, union/padding/minimum duration remain per channel, and finite-value treatment, float64 volts and absolute half-open sample bounds are preserved. The original signal is never overwritten. No threshold, filter, EMG strategy or public payload schema changes.

`ARTIFACT_DETECTION_VERSION` advances to `continuous-artifact-report-v3-exact-ptp`. Existing artifact fingerprints, continuous identities, bounded keys, full/on-demand scientific context and ICA review fingerprints carry this revision, preventing old QC results from being accepted as revised detections.

## Reproduction and automated checks

Before the production edit, the new independent brute-force comparison suite returned 6 failures and 17 passes, including the off-grid example and a public detect/mask failure. Tests cover all 125 former window phases, odd/even widths, short/empty/one-sample sources, first/last windows, strict equality, NaN/infinity handling, padding, minimum duration, offsets and channel isolation. A version test verifies active QC fingerprints change while disabled QC fingerprints remain stable.

Final related backend run: **185 passed** across 19 modules (50.28 s), covering the new 24-case detector suite, report/mask/preview consistency, artifact/bad-channel/removal flows, continuous and bounded filters, official algorithms, on-demand RBP/PSD and file-size policy tests. Two selected scientific architecture boundary tests also pass. The new detector suite was separately rechecked after final comment cleanup: 24 passed. No full backend-directory run or desktop test rerun was performed for this backend-only detector edit.

Repository file-size checking, strict change/spec validation and `git diff --check` pass. `artifact_segments.py` is now 403 physical lines (7 fewer than the prior 410), within its existing 425-line approved responsibility assessment; the contracts file is 338 lines and the new test file 108 lines. No new dependency or additional detector/cache layer was introduced.

## Local real-recording verification

Verification used the previously authorized local recording without changing its files or adding patient data to tracked fixtures. Private numeric evidence is saved only in ignored `.artifacts/artifact-report-fix/exact-ptp-real-validation.json`.

- The formerly missed tail amplitude interval is now detected above the unchanged 300 uV threshold. The report includes the union of exceeded windows plus the existing 100 ms padding.
- Inspection and continuous execution intervals match exactly. The entire prepared NaN mask equals an independently assembled per-channel mask; annotate and reject share their report, and all remaining finite values match exactly.
- The scientific preview returns null values through the newly rejected channel interval. Bounded QC and STFT reject it with `artifact_segment_rejected` / `amplitude_ptp`; PSD rejects non-finite masked input. A clean other channel at the same time remains available to bounded preparation, and a clean-region PSD request succeeds.
- EMG's detected interval remains unchanged. Original source hashes match before/after.
- One local 149982-sample / 21-channel detector run took approximately 0.12 s; this is an observed workstation measurement, not a performance guarantee.

## Limits and deployment

This verifies execution of the declared threshold rule, not clinical artifact classification or detection of every abnormal waveform. No interactive desktop drawing acceptance or hardware test is included. Existing full-height per-channel annotation visuals remain outside this amplitude-detection change. Restart the backend and rescan to obtain the revised report; re-review any ICA confirmation invalidated by the new QC identity. No raw-file conversion or database migration is required.
