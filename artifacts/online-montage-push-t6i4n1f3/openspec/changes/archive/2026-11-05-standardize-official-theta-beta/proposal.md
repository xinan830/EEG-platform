## Why

Official Theta/Beta currently exposes only an IAPF-relative ratio, clips its lower Theta bound to 4 Hz, and has inconsistent dynamic defaults across catalog-driven review and Official Run. The selected frequency definition and analysis window must be explicit so results can be interpreted and reproduced.

## What Changes

- Add standard TBR (Theta 4–8 Hz, Beta 13–30 Hz) and IAF-relative TBR (Theta IAF−6 to IAF−4 Hz, Beta IAF+2 to 30 Hz) as explicit modes; standard mode is the default.
- Remove the individualized Theta lower-bound clamp. Return an unavailable result with a structured reason if a requested band cannot be integrated on the actual frequency axis.
- Set the TBR dynamic policy to a 20-second default, one-second refresh, 20/30-second supported windows, and no partial warm-up results. Apply the same policy to the catalog, Official Run, and Review.
- Record the selected mode, effective bands, and IAF (when used) in backend evidence and provenance; increment the TBR scientific and implementation identities.
- **Compatibility impact:** new Runs default to standard TBR under a new scientific version. Existing Run results remain readable with their recorded identity; no stored-result migration is planned.
- **Non-goals:** aperiodic-corrected TBR, baseline-locked IAF, posterior multi-channel IAF, and clinical interpretation.

## Capabilities

### New Capabilities

### Modified Capabilities
- `analysis/official-algorithm-execution`: define the two band modes, precise individualized boundaries, evidence, and dynamic window contract.
- `desktop/wpf-official-algorithms`: let users select and submit the TBR mode for Official Run.
- `desktop/on-demand-dynamic-review`: let users select the TBR mode in Review and apply the same dynamic window policy.

## Impact

Backend Theta/Beta config, compute, runner, manifest and Official Run request normalization; catalog policy; WPF run parameter binding and serialization; Review scalar parameters and panel; focused backend and desktop tests; OpenSpec contracts. No dependency or database schema change is expected.
