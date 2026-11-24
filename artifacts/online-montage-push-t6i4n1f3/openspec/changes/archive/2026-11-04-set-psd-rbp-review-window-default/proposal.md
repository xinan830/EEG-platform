## Why

The Review PSD and RBP panels currently inherit a generic 5-second dynamic window from the backend policy, so requests do not match the agreed 10-second analysis window. The default belongs to each algorithm's catalog policy because Review serializes that policy directly into backend requests.

## What Changes

- Set the official PSD and RBP dynamic-window defaults to 10 seconds while retaining the 1-second refresh step.
- Keep the Welch segment at 4 seconds with 50% overlap; this change affects only the outer dynamic analysis window.
- Verify both catalog declarations and Review request serialization.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `desktop/on-demand-dynamic-review`: Define the PSD and RBP Review default dynamic analysis window.

## Impact

Affected code is limited to the PSD/RBP backend manifests and focused backend catalog and WPF Review tests. The catalog API shape, scientific formulas, preprocessing, Welch settings, and other algorithms' policies remain unchanged. Existing requests with explicit window values remain valid; only the Review defaults change.
