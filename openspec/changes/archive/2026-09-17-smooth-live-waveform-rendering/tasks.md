## 1. Display pipeline

- [x] 1.1 Cache unchanged display-buffer snapshots.
- [x] 1.2 Aggregate screen buckets across vendor batch boundaries.
- [x] 1.3 Coalesce frame builds off the WPF dispatcher.
- [x] 1.4 Bulk-append completed channel frames to SciChart.

## 2. Verification

- [x] 2.1 Cover cross-batch decimation, gap preservation, and snapshot reuse.
- [x] 2.2 Run desktop build and tests.
- [x] 2.3 Validate OpenSpec strictly and archive the completed change.
