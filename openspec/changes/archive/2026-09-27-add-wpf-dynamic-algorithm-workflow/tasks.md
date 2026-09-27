# Implementation

- [x] 1. Add WPF dynamic mode and policy-backed window/step configuration.
- [x] 2. Submit dynamic official runs through the existing runtime contract.
- [x] 3. Render dynamic scalar series and structured PSD/STFT window summaries.
- [x] 4. Preserve static behavior and unavailable/null semantics.
- [x] 5. Add focused WPF contract/view-model tests.
- [x] 6. Run WPF build/tests, backend regression tests, OpenSpec strict validation,
      and `git diff --check`.

Validation note: WPF compiled successfully in an isolated output directory with
0 warnings and 0 errors. The dynamic view-model/preview tests passed, and the
full desktop suite had 295 passed with one pre-existing timing-sensitive
`HttpLiveFilterBridgeTests.FilterChange...` failure unrelated to this change.
Backend tests passed with `290 passed` from the `backend` working directory.
OpenSpec strict validation passed all 53 items. The normal client output was
locked by a running WPF process, so no running client was interrupted.

## Follow-up implementation

- [x] 7. Enable dynamic RBP as a four-band per-window result without collapsing
      the band values into a single scalar.
- [x] 8. Add dynamic RBP backend and WPF coverage.
- [x] 9. Enable dynamic FAA with a paired-channel window contract and quality
      gate; expose its backend series through the existing WPF dynamic result
      skeleton.
- [x] 10. Add dynamic FAA regression coverage without lowering the static FAA
       quality requirements.

Manual acceptance: completed by the user in the WPF client with a completed
local recording. Dynamic mode, window/step configuration, result rendering,
quality states, and all enabled dynamic official algorithms were verified
without observed errors.
