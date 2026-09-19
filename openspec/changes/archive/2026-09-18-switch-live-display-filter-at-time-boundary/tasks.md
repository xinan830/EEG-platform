## 1. Contract And Implementation

- [x] Replace whole-window display-history replacement with an explicit raw
  sample-counter filter boundary.
- [x] Keep raw EEG persistence independent from the Python display filter.
- [x] Send bounded raw pre-roll to initialize the next Python session without
  returning or displaying its output.
- [x] Segment the rendered trace at a configuration boundary without a time
  gap.
- [x] Supersede an unactivated pending change when a newer setting is selected.

## 2. Verification

- [x] Add desktop batch-slicing and trace-boundary regression tests.
- [x] Run focused Python live-filter tests and desktop Release tests.
- [x] Run Release build, OpenSpec strict validation, and diff check.
