# Why

The amplitude detector advances a half-second window by one quarter of its width. A real recording has a 305.7323 uV T5 peak-to-peak window at 148.050–148.550 s, but the visited neighboring windows remain below 300 uV. Thresholds are therefore missed depending on the window origin.

# What Changes

- Evaluate every valid sample-aligned amplitude window in the shared scientific detector using installed rolling-extrema primitives.
- Preserve per-channel window-union intervals, strict thresholds, finite-value handling, padding, minimum duration, units and absolute sample bounds.
- Advance the shared detection semantic version so prepared signals, bounded gates, algorithm results and confirmed ICA cannot reuse old QC identities.
- Verify against a brute-force oracle, all former stride phases, production consumers and the local real recording.

# Impact

Only amplitude detections change. Existing consumers already share the authoritative recording-level report; no UI/API schema, filter, EMG strategy, raw file or database migration changes. More previously missed windows can be rejected with unchanged thresholds.
