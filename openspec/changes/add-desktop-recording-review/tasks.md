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

## 5. Smooth playback and display controls

- [x] 5.1 Separate the high-frequency playback clock from bounded window loading and prefetch the next page.
- [x] 5.2 Route review high-pass, low-pass, and notch controls through the Python display-filter boundary without changing raw chunks.
- [x] 5.3 Wire review sensitivity and time-base controls into rendering and page duration.
- [x] 5.4 Add regressions proving playback ticks do not reread the visible raw window.
- [x] 5.5 Replace the review time-base selector with paper-speed-derived window duration and a full-recording time navigator.
- [x] 5.6 Pan a continuous visible range over bounded raw/projected caches and coalesce stale navigator requests.
- [x] 5.7 Warm Python causal-filter state from preceding contiguous raw samples without altering raw chunks.

## 6. Derived review cache

- [x] 6.1 Publish the Python display-filter contract for desktop cache fingerprints.
- [x] 6.2 Add immutable source/cache key contracts and atomic on-disk completed filtered chunks outside raw recordings.
- [ ] 6.3 Build fixed filtered source chunks with contiguous warm-up and explicit recorded-gap boundaries. Current 30 s warm-up cap is not a sufficient causal-state guarantee for the supported 0.01 Hz high-pass; add Python checkpoint transfer before marking complete.
- [x] 6.4 Assemble display frames from completed filtered chunks, with a small montage/frame LRU.
- [x] 6.5 Separate navigator preview target from committed waveform target and prefetch nearby chunks without exposing partial data.
- [x] 6.6 Add filter-contract, cache-hit/invalidation, montage-reuse, interrupted-write, and gap-boundary regressions.
- [ ] 6.7 Run backend and desktop verification, OpenSpec strict validation, and diff checks. Full desktop suite currently has an existing live-filter transition test failure.

## 7. Window-local screen scaling

- [x] 7.1 Resolve immutable horizontal and vertical mm-per-DIP values per waveform window and monitor.
- [x] 7.2 Update acquisition and review canvases when their owning window changes display context.
- [x] 7.3 Use width calibration for paper speed and height calibration for vertical EEG sensitivity, with regression coverage.
- [x] 7.4 Centralize shared waveform display settings and geometry calculations while keeping acquisition and review data lifecycles separate.
- [x] 7.5 Centralize shared waveform axis policy while preserving live and review-specific rendering behavior.
- [x] 7.6 Centralize shared waveform plot-area and label alignment geometry without merging acquisition/review state.
