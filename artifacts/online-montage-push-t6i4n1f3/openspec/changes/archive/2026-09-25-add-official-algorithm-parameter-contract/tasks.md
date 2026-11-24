- [x] Add schema constraint fields and validation.
- [x] Enforce schema constraints in the shared runtime.
- [x] Declare official algorithm constraints and add tests.
- [x] Add validated local parameter preset CRUD and migration.
- [x] Define and freeze primitive contracts for PSD, band power, peak frequency,
  and STFT; define sliding windows under the shared execution contract.
- [x] Freeze SI internal units and `[start_sample, end_sample)` canonical range
  semantics, including explicit display conversion rules.
- [x] Define preprocessing, gap, shared quality, and algorithm-specific quality
  contracts.
- [x] Replace analysis-window `warm-up` terminology with a versioned
  `Partial/Complete/Rejected/Unavailable` window state while preserving old
  persisted result readability.
- [x] Distinguish gap imputation from FFT/STFT transform zero-padding in
  contracts and evidence.
- [x] Replace single output-unit assumptions with typed output schemas.
- [x] Add official PeakFrequency and BandRatio modules/presets without
  mislabeling IAPF or standard Theta/Beta semantics.
- [x] Add static, dynamic, and box-selection contract tests with point evidence.
- [x] Define and test display-state isolation from scientific Run identity.
- [x] Define migration behavior for disabling new user-defined execution while
  preserving historical Runs.
- [x] Run backend tests, OpenSpec strict validation, and `git diff --check` for
  the completed parameter/preset slice.
