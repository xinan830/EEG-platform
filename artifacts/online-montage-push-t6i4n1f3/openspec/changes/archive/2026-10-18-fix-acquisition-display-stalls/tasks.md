# Tasks

- [x] Move raw sample payload writes to cancellable asynchronous pooled-buffer I/O.
- [x] Prevent stale frame completion starvation under a busy render cadence.
- [x] Keep the last complete SciChart frame when a replacement frame is unavailable.
- [x] Run acquisition, raw-writer, live-filter, view-model, and frame-builder tests.
- [x] Build the desktop project in an isolated output directory because the running desktop process locks the normal output.
- [x] Manually collect a sustained ANT recording and compare raw audit gaps with visible chart gaps.

Manual acceptance: the user confirmed all outstanding manual checks passed
on 2026-10-18. This is user-reported acceptance, not an agent-observed GUI or
hardware test; no additional recording, duration, or measurement is inferred.
