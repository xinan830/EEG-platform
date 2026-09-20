## 1. Filter handoff

- [x] 1.1 Add binary float64 warm-up transport to the local backend.
- [x] 1.2 Keep the active filter publishing while a replacement warms and catches up.
- [x] 1.3 Activate only at the next sample boundary and preserve earlier display history.
- [x] 1.4 Debounce filter controls and build warm-up snapshots off the WPF dispatcher.
- [x] 1.5 Cover blocked warm-up continuity and backend binary warm-up contracts.

## 2. Sweep presentation

- [x] 2.1 Remove the blue sweep cursor.
- [x] 2.2 Render a white 0.3-second erase range with cyclic wrap.

## 3. Verification

- [x] 3.1 Run focused backend tests.
- [x] 3.2 Run desktop Release tests and build.
- [x] 3.3 Validate this OpenSpec change strictly and run `git diff --check`.
