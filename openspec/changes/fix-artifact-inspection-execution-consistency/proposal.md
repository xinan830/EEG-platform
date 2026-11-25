# Proposal

## Why

Real-recording acceptance confirmed that the artifact list reports tail PTP segments absent from the processed NaN mask. Inspection uses an independent cropped 0.5–45 Hz pipeline while preview and algorithms redetect with different filtering and channel populations.

## What Changes

- Make continuous pre-removal detection the authoritative report for inspection, reject masks and bounded algorithm gates. Reuse the annotated prepared source instead of redetecting in each consumer.
- Inspection exposes its scientific filters and full-recording detection scope; response ranges remain absolute, bounded report projections.
- Invalidate displayed scan results when detection inputs change, preserving them across policy/removal changes that do not change detection.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `signal/preprocessing`: Consistent artifact reports, channel-specific masks and bounded quality gates.

## Impact

Affected: preprocessing facade, continuous mapped preparation/cache, inspection API, bounded STFT/TNK callers, desktop scan invalidation and regression tests. API defaults align with Review scientific preview (1–50 Hz, 50 Hz notch); explicit filters are accepted. Automatic intervals remain automatic, never converted into manual annotations. No database migration, dependency, raw-file change, display-filter change or algorithm formula change. Deploy backend and desktop together; rollback both. Full-recording QC may incur a cold preparation cost for bounded algorithms only when artifact QC is enabled; reuse the existing bounded disk-backed cache and cancellation/resource controls.
