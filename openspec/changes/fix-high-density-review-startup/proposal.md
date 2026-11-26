# Proposal

## Why
Opening the imported 264-channel BDF repeatedly validates the 128-channel CAR source mapping by scanning complete channel lists for every reference dependency. A profiled recording-summary read takes 19.67 seconds and 8.7 million field lookups. The desktop waits for its first frame before showing the review window and admits repeated opening clicks, multiplying the same work.

## What Changes
- Index immutable channel bindings once and validate each distinct dependency once per resolution, retaining ambiguous/native/type/unit rejection.
- Show the review surface before awaiting the first frame and admit only one opening operation; keep close/cancellation and failure cleanup owned by the shell.
- Clear obsolete filtering status when the next source chunk is being read.
- Resolve imported montage display identity from its validated embedded channel snapshot when optional saved fingerprints are absent; reject malformed snapshots before catalogue/display code uses them, and log review failures with their request context.

## Capabilities
### Modified Capabilities
- `desktop/recording-review`: responsive single-flight review startup and truthful loading state.

## Impact
Backend montage resolution and desktop review startup/loading only. No changes to raw recordings, sample times/order, V/µV units, filters, scientific algorithms, API or WebSocket payloads. No dependencies or database migration. Preserve unrelated workspace changes.
