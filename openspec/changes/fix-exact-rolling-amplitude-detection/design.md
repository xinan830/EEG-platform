# Design

Replace the quarter-window start grid in `_detect_amplitude_ptp_segments` with per-channel forward maximum/minimum filters from the installed SciPy dependency. Align each result to [start, start + window_samples) and retain only full in-recording windows (use the complete recording when shorter than the requested window). No dense samples-by-window matrix is allocated.

Ignore non-finite input for extrema, as before, using opposite infinity sentinels only within detection calculations. They never enter output signals; no finite pair or an all-missing window cannot exceed a positive threshold. Group contiguous exceeded window starts, extend through the last window end, and merge overlapping windows only within the same channel. Apply the existing minimum-duration and pad rules after union.

Keep absolute half-open sample coordinates, sample-major float64 volts and API microvolts unchanged. Increment ARTIFACT_DETECTION_VERSION, already consumed by artifact configuration, prepared caches, bounded QC, ICA and shared algorithm identities. The detector is the sole production fix location; inspection/preview/algorithms continue to consume its shared report.

Tests use an independent small brute-force enumerator, every old 125-sample stride phase, odd/even widths, short recordings, first/last windows, strict threshold equality, non-finite samples, offsets, padding and channel isolation. Local real-data validation checks detection, exact masks, preview nulls and bounded rejection; no patient data is added to tracked files. Business files must remain within their registered size limits.

Rollback detector and semantic version together. Existing artifacts/results are retained under their prior identities; fresh requests use the revised detector version.

## Size assessment

The scientific detector module shrinks from 410 to 403 physical lines and retains its existing 425-line `keep` assessment in `docs/code-size-policy.json`: amplitude, muscle, manual intervals and their common mask/merge contract remain one responsibility. No mechanical split or increased exception is needed. The contract module remains below 400 lines.
