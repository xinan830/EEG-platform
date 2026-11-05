## Why

The Review page currently declares the same algorithm set in separate coordinator composition, panel registration, ViewModel command/state, and XAML presentation locations. This makes wiring harder to audit and allows an algorithm to be exposed without a matching Review adapter or panel registration.

## What Changes

- Centralize Review adapter/coordinator creation inside the Review module instead of constructing the algorithm list in `MainWindow.xaml.cs`.
- Make the Review panel registration and coordinator registrations verifiably agree by `AlgorithmId`.
- Derive active panel title, scalar presentation and template selection from that registration, retaining explicit XAML templates and entry controls.
- Preserve algorithm-owned adapters and specialized PSD/RBP/TNK presentation behavior.
- Add focused desktop tests for the complete algorithm identity mapping and unsupported/mismatched registration behavior.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/review-algorithm-link`: clarify the identity and registration contract between surfaced Review panels and their matching algorithm adapters.

## Impact

- Affected code: WPF Review composition and view-model registration, `MainWindow.xaml.cs`, and focused Review tests.
- Backend algorithms, scientific formulas, request/response APIs, units, filter settings, and persisted data are unchanged.
- Existing Review panels and command names remain compatible; no migration is required.
- Verification is limited to targeted desktop Review tests, desktop build, OpenSpec validation, and `git diff --check`; full test suites and manual WPF interaction are outside this change.
