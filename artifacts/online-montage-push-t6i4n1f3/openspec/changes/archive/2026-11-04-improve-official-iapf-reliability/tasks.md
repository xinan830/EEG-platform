## 1. Scientific contract and estimator

- [x] 1.1 Add deterministic synthetic tests for local Alpha peaks, no-peak spectra, broad peaks, and edge bins.
- [x] 1.2 Implement independent PAF prominence/width and COG integrated-excess gates; return structured unavailable reasons when neither passes.
- [x] 1.3 Bump the IAPF scientific/implementation identity and keep diagnostic evidence explicit, including dependent Official Theta/Beta and BrainBeat identities.

## 2. Dynamic window policy

- [x] 2.1 Set the IAPF catalog default to 20 seconds, one-second refresh, and no partial warm-up values.
- [x] 2.2 Verify window scheduling and accepted Welch-segment averaging at 20, 21, and 22 seconds.
- [x] 2.3 Update catalog and runtime regression tests for the IAPF-specific policy.

## 3. Validation

- [x] 3.1 Run IAPF and directly affected backend tests, excluding the full suite.
- [x] 3.2 Run strict OpenSpec validation and `git diff --check`; report engineering validation limits.
