## Contract

- [x] Define v3 RBP channel-aware result and window payloads.
- [x] Update manifest, Definition identity, and algorithm configuration validation.
- [x] Add per-channel quality, unit, order, and provenance tests.

## Backend

- [x] Resolve all selected montage outputs and batch spectral loading per window.
- [x] Serialize channel-aware full and on-demand results.
- [x] Add multi-channel RBP API and parity tests.
- [x] Add a startup regression test for preserving Definition 2.0.1 and creating 2.0.2.

## Desktop

- [x] Decode channel-aware RBP results and reset history on context changes.
- [x] Append window results by scientific identity with bounded history.
- [x] Render one 100% stacked history chart per montage output.
- [x] Add focused projection and WPF chart tests.
- [x] Append visited RBP windows into stable per-channel collections and defer on-demand loading until a timeline drag commits.
- [x] Verify the combined full-run structured-preview path against an RBP v3 fixture, including per-window channel failure evidence propagation.

## Verification

- [x] Re-run focused backend tests after the latest edits: startup Definition migration 1 passed; RBP five-band and on-demand API tests 6 passed.
- [ ] Finish WPF RBP tests. Latest run: 22 passed, 1 failed; `Combined_rbp_serves_completed_structured_full_window_without_http_preview` does not reach structured-preview loading.
- [x] Run OpenSpec strict validation and `git diff --check` after the latest edits.
- [x] Build the desktop test project in an isolated output directory (0 errors; dependency compatibility warnings remain).
- [ ] Complete interactive GUI and real-recording validation. Not performed; report this limitation at delivery.

