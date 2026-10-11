# Tasks

- [x] Move raw sample payload writes to cancellable asynchronous pooled-buffer I/O.
- [x] Prevent stale frame completion starvation under a busy render cadence.
- [x] Keep the last complete SciChart frame when a replacement frame is unavailable.
- [x] Run acquisition, raw-writer, live-filter, view-model, and frame-builder tests.
- [x] Build the desktop project in an isolated output directory because the running desktop process locks the normal output.
- [ ] Manually collect a sustained ANT recording and compare raw audit gaps with visible chart gaps.
