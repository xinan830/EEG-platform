## Context

Review initializes `ReviewPsdParameters` and `ReviewRbpParameters` from the backend algorithm catalog and serializes `default_window_s` as `dynamic_window_s`. Both PSD and RBP currently omit an explicit manifest policy, so they inherit the generic five-second default. That shared default must remain unchanged for other algorithms.

## Decision

Declare a ten-second dynamic-window policy directly in the PSD and RBP manifests, with the existing one-second refresh step. Preserve the shared minimum window, supported options, and warmup behavior unless required by the declared ten-second default. Do not change the scientific spectral contract: Welch remains four seconds with 50% overlap.

## Risks and Compatibility

Only catalog-driven defaults change. Existing explicit request values, API shape, historical results, filter settings, and other algorithm policies remain unchanged. Review users will see results based on a longer default context and may wait until enough data is available for a full window, subject to the existing warmup behavior.

## Validation

Test the catalog's PSD/RBP policies and the serialized dynamic-window values from Review parameter objects. Run only the focused backend catalog and desktop Review tests, plus strict OpenSpec validation and `git diff --check`.
