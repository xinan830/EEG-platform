## 1. Device Session

- [x] 1.1 Add session state/snapshot contract and manager around configured runtime.
- [x] 1.2 Derive a deterministic capability fingerprint from returned common channel capabilities.
- [x] 1.3 Translate discovery and runtime state/fault transitions into snapshots.
- [x] 1.4 Preserve SDK-reported device model and refresh an idle configured session for device removal.
- [x] 1.5 Bind reusable channel configurations to driver ID plus full physical capabilities, never device instances.

## 2. Desktop Consumers

- [x] 2.1 Make acquisition setup bind to the shared device session.
- [x] 2.2 Make channel-profile drafts resolve the selected device through the shared device session.
- [x] 2.3 Remove navigation-time device passing and duplicate selected-device state.
- [x] 2.4 Extract waveform-display preferences from the acquisition lifecycle view model.

## 3. Verification

- [x] 3.1 Test discovery selection, no-device clearing, capability fingerprint stability, and runtime fault state.
- [x] 3.2 Test channel configuration resolves the shared selected descriptor.
- [x] 3.3 Run desktop Release build, desktop tests, strict OpenSpec validation, and diff check.
- [x] 3.4 Test that an idle availability refresh clears a removed device without a page action.
