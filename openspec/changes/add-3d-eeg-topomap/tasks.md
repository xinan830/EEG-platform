## 1. Mapping and model inputs

- [x] 1.1 After the user supplies the standard mapping file, verify its exact MNE source montage, coordinate frame/unit, explicit transform, electrode coverage, target GNM mesh identity, target frame/unit, scalp region, and intended-use provenance.
- [x] 1.2 Add the verified mapping and licensed model asset to the application distribution with stable IDs and SHA-256 identities; add coordinate and asset integrity fixtures from the supplied standard file.
- [x] 1.3 Test canonical electrode ordering, unsupported labels, REF/GND exclusion, coordinate transforms, surface bounds, and rejection of mismatched frames, units, and mesh hashes.

## 2. Backend 3D contracts

- [x] 2.1 Add a typed 3D electrode-location response that keeps the current 2D resolver and manual `MapX`/`MapY` contract unchanged.
- [x] 2.2 Add a typed model and 3D topomap response with stable mesh vertex ordering, coordinate provenance, band units, absolute window, included/excluded channels, quality, and structured unavailable reasons.
- [x] 2.3 Select and document a deterministic bounded surface interpolation method against the supplied mapping and mesh; preserve source values at mapped electrode sites and add synthetic-field tests.
- [x] 2.4 Build RBP 3D fields from existing five-band window results without rerunning preprocessing or formulas; add full-run and dynamic-window API paths and tests for stale/missing/invalid results.
- [x] 2.5 Serve the canonical model asset once by model identity and verify that API metadata, mapping, and mesh use the same content hashes.

## 3. Desktop renderer and RBP integration

- [x] 3.1 Add a reusable WPF 3D scalp view that loads and caches the verified model asynchronously, supports camera interaction, and rejects mismatched model/vertex identities.
- [x] 3.2 Add an independent 3D presentation to the RBP topomap host with a five-band selector; preserve the current 2D topomap as a separate view.
- [x] 3.3 Prepare scene updates away from the UI dispatcher, retain the last complete visual while preparing, and reject superseded playback windows.
- [x] 3.4 Add desktop contract and rendering tests.
- [ ] 3.5 Verify one-minute playback does not block waveform updates or introduce recurring UI-thread work.
- [x] 3.6 Move the immutable 3D scalp model contract and cached model request into the shared electrode-location capability; use the same client from RBP and Channel Settings.
- [x] 3.7 Add a 2D/3D switch to the Channel Settings electrode-position window; show only enabled mapped EEG inputs in 3D and keep 2D edits, REF, and GND out of the 3D coordinate path.
- [x] 3.8 Center display geometry around the mesh bounds, frame the initial camera from the model bounds, and test model-only electrode rendering and frame calculations.
- [ ] 3.9 Manually verify that the Channel Settings toggle, enabled-channel positions, and return to the editable 2D diagram work with a real configured device profile.
- [x] 3.10 Show saved profile EEG channels when no device draft is loaded; keep that preview read-only and add a regression test.
- [x] 3.11 Separate mapped coordinate availability from `is_scalp_field_site`; display mapped A1/A2/M1/M2 positions while preserving their exclusion from RBP scalp-field interpolation.
- [x] 3.12 Keep the camera outside an origin-centered sphere enclosing the full scene; zoom through field-of-view changes and test repeated zoom-in.
- [x] 3.13 Keep Channel Settings 3D uncluttered by showing mapped electrode markers without floating channel-name labels or connector lines; retain channel names in the adjacent list.

## 4. Verification and documentation

- [x] 4.1 Run focused backend tests, desktop tests, and the WPF build; validate coordinate frames, units, channel order, exact electrode positions, interpolation boundaries, stale-window rejection, and the shared Channel Settings model path.
- [ ] 4.2 Run OpenSpec strict validation and `git diff --check`; document the selected mapping/model hashes, spatial method, licensing notice, and manual playback acceptance.
