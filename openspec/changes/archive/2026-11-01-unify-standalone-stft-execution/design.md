## Context

`StftAlgorithm` is registered in the builtin factory and already enters `AlgorithmRuntime`, Run queue and on-demand services. The services own compute leases, cache, generations, subscriptions and result persistence. The WPF standalone page uses the common catalog and existing STFT preview handler. No new transport is needed.

Duplication lives in `stft/runner.py` static/dynamic methods and `stft/window.py`. The source is `RecordingAlgorithmContext.load_spectrogram`, which constructs a typed sample-bounded request before invoking the shared scientific service. Continuous preprocessing can still prepare the whole selected recording: bounded result coordinates do not imply bounded preprocessing.

## Decisions

1. Add `stft/frame.py`: one immutable typed outcome with recording-absolute times, frequency axis, V^2/Hz and dB matrices, actual range, quality, structured failure, evidence and provenance. One source load per frame. Preserve project spectral primitives; do not duplicate FFT/filter formulas.
2. Validate coordinates, shapes, finite clean rows and nonnegative linear power once. Recognized bad inner rows may contain NaN; never accept infinity, malformed axes or an unmarked nonfinite row as science. Preserve source quality evidence. Static output retains real clean rows and bad-row NaNs. Dynamic outer-window policy remains strict: any bad inner row rejects the outer window for both full and on-demand, removing the prior strategy discrepancy.
3. Adapters map shared results: static retains absolute center times; windows use relative centers; full dynamic alone pads short warmup slots with NaN. Rejected/unavailable full slots contain no finite science, while on-demand returns empty axes/arrays plus failure. Preserve warmup state separately from quality.
4. Full cache reuse still requires complete verified windows with current relative axes and matching configuration. Cancellation is checked before and after preparation, outside conversion/error catches; cancellation exceptions propagate. Full iteration continues using `controlled_frames` and platform execution control.
5. Current science becomes `spectrogram-v3-bounded`, implementation `stft-runtime-v6-bounded`, policy `stft-dynamic-window-v3-bounded`, immutable Definition 1.1.0. No old executor or result conversion is added. Historic artifacts remain stored but cannot seed current caches.
6. Extract the existing finite-context reader/filter/crop operation into a shared service primitive, preserving TNK's explicit two-second context and version. STFT declares context `max(2, 3 / low_hz)` seconds per side: at least three low-cut cycles, rounded up to samples, clamped to recording boundaries. Apply the existing notch then fourth-order zero-phase Butterworth filtering, crop to target, and retain the existing strict Welch gate and per-STFT-row quality rules. Context gaps cause explicit rejection. This is a reproducible finite contract, not a guarantee of equivalence to infinite or whole-record filtering.
7. STFT source requests, execution snapshots, service/API envelopes, frame evidence, cache identities and WPF scope checks all declare bounded preprocessing. Extract spectrogram source assembly from the oversized spectral service. Validate parameters before reads, close source sessions on all exits, and check cancellation around filtering/publication. No recordings, schemas, queues or transport change.

## Alternatives

- Wrapping the existing three implementations keeps inconsistent validation/failures and is rejected.
- Building a single-node TNK graph or new queue duplicates platform admission without useful dependencies and is rejected.
- Filtering the target without context or silently reusing the old scientific version is rejected. Keeping whole-record preprocessing behind another reader does not remove first-window dependency.

## Validation and Migration

Run baseline STFT tests first. Add static/full/window parity at nonzero absolute time, source request counts, malformed data, raised/returned quality, warmup padding, cancellation and later-window recovery. Run existing numerical periodogram comparisons, full/window directional reuse, API/combined strategy and compute coordination tests. Verify desktop STFT handlers/preview bindings through focused tests and BAML build; user manual playback acceptance remains separate. Record exact outcomes and any unrelated failures. Validate OpenSpec strictly and check diff/file sizes. Raw data and historical results are preserved; rollback is a previous build.

Existing active changes belong to prior RBP/waveform work; this separately authorized STFT scope must not rewrite or archive them. The advisory preference to finish all active changes before opening another is inapplicable to those unrelated/manual-acceptance changes.

Bounded validation additionally spies on reads for synthetic 24-hour metadata without allocating a 24-hour array, forbids whole-record preparation, covers start/end clipping, channel order, gaps and cancellation, and compares bounded filter/FFT results with independently assembled SciPy references. Quantify differences against whole-record filtering without asserting equivalence. Equal requested sample bounds must match across strategies; different overlapping outer ranges may differ because their filter boundaries differ. A deliberately requested whole-record static analysis remains proportional to that requested range.
