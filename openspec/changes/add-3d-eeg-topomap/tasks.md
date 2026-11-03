## 1. Mapping and model inputs

- [ ] 1.1 After the user supplies the standard mapping file, verify its exact MNE source montage, coordinate frame/unit, explicit transform, electrode coverage, target GNM mesh identity, target frame/unit, scalp region, and intended-use provenance.
- [ ] 1.2 Add the verified mapping and licensed model asset to the application distribution with stable IDs and SHA-256 identities; add coordinate and asset integrity fixtures from the supplied standard file.
- [ ] 1.3 Test canonical electrode ordering, unsupported labels, REF/GND exclusion, coordinate transforms, surface bounds, and rejection of mismatched frames, units, and mesh hashes.

## 2. Backend 3D contracts

- [ ] 2.1 Add a typed 3D electrode-location response that keeps the current 2D resolver and manual `MapX`/`MapY` contract unchanged.
- [ ] 2.2 Add a typed model and 3D topomap response with stable mesh vertex ordering, coordinate provenance, band units, absolute window, included/excluded channels, quality, and structured unavailable reasons.
- [ ] 2.3 Select and document a deterministic bounded surface interpolation method against the supplied mapping and mesh; preserve source values at mapped electrode sites and add synthetic-field tests.
- [ ] 2.4 Build RBP 3D fields from existing five-band window results without rerunning preprocessing or formulas; add full-run and dynamic-window API paths and tests for stale/missing/invalid results.
- [ ] 2.5 Serve the canonical model asset once by model identity and verify that API metadata, mapping, and mesh use the same content hashes.

## 3. Desktop renderer and RBP integration

- [ ] 3.1 Add a reusable WPF 3D scalp view that loads and caches the verified model asynchronously, supports camera interaction, and rejects mismatched model/vertex identities.
- [ ] 3.2 Add an independent 3D presentation to the RBP topomap host with a five-band selector; preserve the current 2D topomap as a separate view.
- [ ] 3.3 Prepare scene updates away from the UI dispatcher, retain the last complete visual while preparing, and reject superseded playback windows.
- [ ] 3.4 Add desktop contract/rendering tests and verify one-minute playback does not block waveform updates or introduce recurring UI-thread work.

## 4. Verification and documentation

- [ ] 4.1 Run focused backend tests, desktop tests, and the WPF build; validate coordinate frames, units, channel order, exact electrode positions, interpolation boundaries, and stale-window rejection.
- [ ] 4.2 Run OpenSpec strict validation and `git diff --check`; document the selected mapping/model hashes, spatial method, licensing notice, and manual playback acceptance.
