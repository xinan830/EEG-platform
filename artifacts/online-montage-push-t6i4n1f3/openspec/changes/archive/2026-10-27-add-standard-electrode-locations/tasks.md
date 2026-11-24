## 1. Profile compatibility and coordinate contract

- [x] 1.1 Confirm supported MNE standard montage names, site coverage, coordinate frame, bundled data provenance/license, and MNE/data version; document the resulting resolution contract without copying a second standard coordinate table.
- [x] 1.2 Define a typed backend location-resolution boundary and explicit label normalization/failure behavior; keep MNE objects internal to the backend.
- [x] 1.3 Add optional electrode-location snapshot fields to channel entries and verify legacy profile/recording JSON remains readable with location unavailable.
- [x] 1.4 Add fingerprint coverage and tests for location identity, legacy compatibility, duplicate sites, unsupported labels, and coordinate/source-version validation.

## 2. Settings and snapshot behavior

- [x] 2.1 Resolve supported 10–20 labels through the backend MNE adapter into explicit standard-template metadata and show location status in editable channel settings.
- [x] 2.2 Persist the resolved location on save and preserve it through acquisition/recording snapshots without changing sample units, channel order, reference processing, or raw data.
- [x] 2.3 Add backend and desktop contract tests for recognized, unsupported, reference/ground, and duplicate labels plus historical snapshot independence after profile edits.

## 3. Verification and documentation

- [x] 3.1 Run focused backend and desktop tests, the desktop build, this change's OpenSpec strict validation, and `git diff --check`.
- [x] 3.2 Document the MNE/data source and version, coordinate frame, unavailable-location behavior, and that verification is engineering-only rather than clinical validation.

## 4. Position illustration

- [x] 4.1 Add a head-top illustration in channel settings using only valid persisted standard-template coordinate snapshots.
- [x] 4.2 Isolate and test MRI X/Y projection orientation and invalid/unavailable filtering; verify label changes and resolution updates refresh the view.
- [x] 4.3 Run focused desktop tests/build, strict validation for this change, and `git diff --check`.
- [x] 4.4 Add a modeless floating map window with selected-channel placement, and persist normalized 2D manual positions separately from standard XYZ snapshots.
- [x] 4.5 Verify floating-window interaction, manual placement save/reload, label-change clearing, display-toggle filtering, focused desktop tests/build, strict validation, and `git diff --check`.
