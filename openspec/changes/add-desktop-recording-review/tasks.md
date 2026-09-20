## 1. Contract and storage

- [x] 1.1 Add typed manifest, audit, chunk-index, and review-window contracts.
- [x] 1.2 Add a bounded local raw reader with counter-based time and explicit gap/corruption handling.
- [x] 1.3 Extend project recording rows with stable selection and review eligibility.

## 2. Montage review

- [x] 2.1 Add acquisition-snapshot parsing and current-profile compatibility evaluation.
- [x] 2.2 Extract immutable montage display projection for review windows.
- [x] 2.3 Preserve absolute position and duration across montage output-count changes.

## 3. Full-screen workspace

- [x] 3.1 Add the project-recording `回溯` action and immersive navigation lifecycle.
- [x] 3.2 Add recording review state, toolbar, waveform canvas, playback, seeking, and back navigation.
- [x] 3.3 Add load, empty, incompatible, corrupt, and legacy-recording states without synthetic data.

## 4. Verification

- [x] 4.1 Add reader, compatibility, montage switching, project navigation, and performance-regression tests.
- [x] 4.2 Run desktop Release tests/build, strict OpenSpec validation, file-size review, and `git diff --check`.
