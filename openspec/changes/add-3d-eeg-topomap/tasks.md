## 1. Mapping and model inputs

- [x] 1.1 Verify the supplied mapping's MNE source montage, coordinate frame/unit, electrode coverage, target GNM mesh identity, target frame/unit, and intended use.
- [x] 1.2 Package the verified mapping and licensed model asset with stable identifiers and SHA-256 identities.
- [x] 1.3 Test channel ordering, aliases, unsupported labels, REF/GND exclusion, coordinate transforms, and model identity validation.

## 2. Shared backend model capability

- [x] 2.1 Add a typed 3D electrode-location response independent of the existing 2D MapX/MapY contract.
- [x] 2.2 Add a typed model response with stable mesh ordering, mapping provenance, source/target coordinates, and scalp-field-site classification.
- [x] 2.3 Serve the canonical model and exact location resolution through read-only endpoints.

## 3. Channel Settings 3D view

- [x] 3.1 Add an asynchronous WPF head-model view with camera interaction and model/mesh identity validation.
- [x] 3.2 Add a 2D/3D switch beside the existing editable Channel Settings diagram.
- [x] 3.3 Center the display geometry and frame the camera from the model bounds.
- [x] 3.4 Show saved-profile EEG channels when no device draft is loaded.
- [x] 3.5 Show mapped A1/A2/M1/M2 markers while keeping scalp-field eligibility separate.
- [x] 3.6 Bound zoom and retain Shift-drag pan without floating electrode-name labels.
- [ ] 3.7 Manually verify the Channel Settings switch, enabled-channel positions, and return to the editable 2D diagram with a real configured profile.

## 4. Remove the RBP 3D scalp-field path

- [x] 4.1 Remove the RBP 3D selector, playback refresh, and field-rendering branch.
- [x] 4.2 Remove RBP-only 3D interpolation endpoints, service methods, and response contracts.
- [x] 4.3 Keep the shared model and location endpoints required by Channel Settings.

## 5. Verification

- [ ] 5.1 Run focused backend tests, desktop tests, and a WPF build.
- [ ] 5.2 Run OpenSpec strict validation and git diff --check; record the mapping/model hashes and manual Channel Settings acceptance.
