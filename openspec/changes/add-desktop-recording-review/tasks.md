## 1. Contract and storage

- [ ] 1.1 Add typed manifest, audit, chunk-index, and review-window contracts.
- [ ] 1.2 Add a bounded local raw reader with counter-based time and explicit gap/corruption handling.
- [ ] 1.3 Extend project recording rows with stable selection and review eligibility.

## 2. Montage review

- [ ] 2.1 Add acquisition-snapshot parsing and current-profile compatibility evaluation.
- [ ] 2.2 Extract shared immutable montage display projection and use it for review windows.
- [ ] 2.3 Preserve absolute position and duration across montage output-count changes.

## 3. Full-screen workspace

- [ ] 3.1 Add the project-recording `回溯` action and immersive navigation lifecycle.
- [ ] 3.2 Add recording review state, toolbar, waveform canvas, playback, seeking, and back navigation.
- [ ] 3.3 Add load, empty, incompatible, corrupt, and legacy-recording states without synthetic data.

## 4. Verification

- [ ] 4.1 Add reader, compatibility, montage switching, project navigation, and performance-regression tests.
- [ ] 4.2 Run desktop Release tests/build, strict OpenSpec validation, file-size review, and `git diff --check`.

