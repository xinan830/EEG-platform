# Tasks

- [x] 1. Expose static and dynamic backend band-share arrays with labels, ranges, units, and provenance.
- [x] 2. Parse backend shares in WPF without client-side spectral calculation or normalization.
- [x] 3. Add an independent SciChart band-share view and chart selection to the PSD detail page.
- [x] 4. Cover backend and WPF result paths with automated tests and validate the OpenSpec change.
- [x] 5. Restart WPF and visually confirm static and dynamic chart switching with a local recording.

Manual acceptance: the user confirmed all outstanding manual checks passed
on 2026-10-18. This is user-reported acceptance, not an agent-observed GUI or
hardware test; no additional recording, duration, or measurement is inferred.

Validation: backend targeted tests 36 passed; WPF tests 324 passed; isolated WPF build succeeded. The manual chart check remains open.
