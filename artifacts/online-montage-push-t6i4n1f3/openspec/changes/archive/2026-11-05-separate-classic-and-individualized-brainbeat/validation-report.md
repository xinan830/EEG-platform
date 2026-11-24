# BrainBeat Validation Report

## Scope

Separate classic absolute-power BrainBeat from the IAPF-relative variant and
repair individualized BrainBeat result projection in recording review.

## Verification

- BrainBeat on-demand desktop tests: 4 passed.
- Recording-review scalar panel registration tests: 5 passed.
- OpenSpec strict validation after main-spec synchronization: 85 items passed.
- `git diff --check`: passed.
- Tests used an isolated output directory because running desktop processes
  locked the default WPF build outputs.

## Limits

- No full test suite was run.
- No live recording GUI playback was performed in this validation pass.
- OpenSpec task 1.1, verification of Holm et al. primary methods, remains
  incomplete. Do not claim exact literature replication based on this report.
