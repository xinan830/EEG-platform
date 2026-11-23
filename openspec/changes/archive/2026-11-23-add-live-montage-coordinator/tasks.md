# Tasks

## 1. Coordinator contract

- [x] 1.1 Add the immutable live montage snapshot and session-owned coordinator with compatible-profile checks and monotonic revisions; verify assignment, rejection, and revision tests.
- [x] 1.2 Trace and test the current live analysis channel contract against montage outputs; document any required realtime message metadata before changing consumers.

## 2. Acquisition session integration

- [x] 2.1 Make the acquisition runtime own and initialize the coordinator from the frozen acquisition snapshot; verify preview/recording lifecycle tests preserve the writer snapshot.
- [x] 2.2 Serialize montage updates with sample batch dispatch and pass one captured revision to waveform projection and live analysis; verify channel order, V units, sample counters, timestamps, and no mixed-revision windows.

## 3. Live algorithm consumers

- [x] 3.1 Resolve supported live algorithm inputs and topography metadata from the shared montage snapshot without duplicating formulas; verify backend live contract tests for compatible and unavailable mappings.
- [x] 3.2 Reject stale live results after a montage revision change and clear/reset old rolling state; verify a delayed old-revision result cannot replace the active view.

## 4. Integration verification

- [x] 4.1 Update file-size policy and dated validation evidence for changed business files; verify no changed hand-written file exceeds policy without a documented review.
- [x] 4.2 Run focused backend and desktop tests, isolated desktop build when output files are available, `git diff --check`, and strict OpenSpec validation; record any hardware-only limitation.
