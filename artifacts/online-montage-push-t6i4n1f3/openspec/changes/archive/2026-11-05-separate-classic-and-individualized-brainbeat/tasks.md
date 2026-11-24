## 1. Contract and Regression Baseline

- [ ] 1.1 Verify Holm et al. primary methods for bands, absolute/relative power, and spectral estimator; record accessible source and any unresolved limits without asserting unverified paper details.
- [x] 1.2 Add failing synthetic numerical tests proving fixed-band classic absolute-power ratio and invariance to out-of-band power changes.
- [x] 1.3 Add identity/default contract tests for distinct `brainbeat` and `brainbeat_individualized` entries, 20-second window, one-second step, and supported window values.
- [x] 1.4 Add compatibility tests proving historical BrainBeat Run payloads remain byte-for-byte readable and are not routed through a retired calculator.

## 2. Backend Scientific Modules and Catalog

- [x] 2.1 Implement classic Fz 4–8 Hz / Pz 8–12 Hz absolute-power ratio using the shared boundary-interpolated band integration primitive and explicit intermediate units/evidence.
- [x] 2.2 Register the existing IAPF-relative calculation under `brainbeat_individualized`, keeping its IAPF failure/null behavior and relative-power denominators explicit.
- [x] 2.3 Version both scientific identities and declare matching catalog schemas, role order, preprocessing/Welch contracts, quality semantics, and 20/1-second dynamic policies.
- [x] 2.4 Update Official Run validation, runtime configuration, dynamic request planning, and cache identity so each variant selects only its own current formula and window policy.
- [x] 2.5 Verify both variants retain paired Fz/Pz channel order, sample ranges, notch options, non-finite/missing-sample rejection, and the shared minimum-clean-segment gate.

## 3. Desktop Run and Recording Review

- [x] 3.1 Update algorithm catalog/detail registration and Run serialization to expose classic and individualized variants as distinct selectable identities.
- [x] 3.2 Add a recording-review variant choice that sends the selected identity and catalog-declared window settings; do not duplicate scientific calculations in WPF.
- [x] 3.3 Update BrainBeat result presentation to show the selected variant, backend-returned actual range, ratio, quality state, failure reason, and formula evidence.
- [x] 3.4 Add desktop contract tests for variant selection, request identity, 20/1-second defaults, channel role ordering, and null/rejected presentation.

## 4. End-to-End Verification and Documentation

- [x] 4.1 Test static and dynamic classic results against independent synthetic reference values and individualized results against their existing formula fixtures.
- [x] 4.2 Test 20, 21, and 22-second windows, complete-window warm-up behavior, quality rejection, and Run/on-demand parity for identical actual ranges.
- [x] 4.3 Test catalog identity, persisted result version/evidence, and cache separation between classic, individualized, and historical runs.
- [x] 4.4 Update current BrainBeat algorithm documentation and source ledger with verified formula, units, parameter defaults, source citations, and explicit scientific-validation limits.
- [x] 4.5 Run focused backend BrainBeat/Run/on-demand/catalog tests, focused desktop tests/build, OpenSpec strict validation, and `git diff --check`; report any environment-limited checks.
