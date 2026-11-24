# Tasks

- [x] 1. Add STFT frequency and notch fields to the validated Run contract.
- [x] 2. Connect the WPF shared controls to STFT Run configuration.
- [x] 3. Apply range, filtering, and notch settings in the STFT backend path.
- [x] 4. Record selected settings in STFT execution evidence and provenance.
- [x] 5. Add backend and WPF regression coverage.
- [x] 6. Restart WPF and visually verify the shared controls and 1-50 Hz STFT output.

Manual acceptance: the user confirmed all outstanding manual checks passed
on 2026-10-18. This is user-reported acceptance, not an agent-observed GUI or
hardware test; no additional recording, duration, or measurement is inferred.

Validation completed: backend targeted tests `45 passed`; WPF algorithm tests
`62 passed`; `git diff --check` pending.
