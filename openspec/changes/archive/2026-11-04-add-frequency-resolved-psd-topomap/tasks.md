## 1. Freeze the current contracts

- [x] 1.1 Add PSD schema tests for ordered channel inputs, shared frequency axes, static `[channel, frequency]` and dynamic `[window, channel, frequency]` shapes, plus per-channel status alignment.
- [x] 1.2 Add topomap API contract tests for explicit `field_ids`, `fields[]`, exact PSD frequency metadata, RBP relative/absolute units, and rejection of unknown fields without legacy query handling.
- [x] 1.3 Define and test the current PSD result identity/version boundary so historical single-channel Runs cannot be reused as multi-channel output.
- [x] 1.4 Add boundary-aligned numerical tests proving PSD and Brainbeat use the same shared band integration primitive; preserve Brainbeat-specific bands, denominator, and four-second Welch window.

## 2. Implement channel-resolved PSD results

- [x] 2.1 Update the PSD manifest, config, parameter schema, and runtime validation for a nonempty ordered channel set while preserving the existing Welch, filtering, window, and preprocessing formulas.
- [x] 2.2 Produce channel-resolved static, dynamic, and one-window on-demand arrays on one shared frequency axis, with a distinct quality state and reason for each channel/window.
- [x] 2.3 Keep a failed channel local to itself; return valid neighboring channel results and verify no zero, neighboring, or previous-window substitution occurs.
- [x] 2.4 Add multi-channel scientific reference tests for numerical parity, exact channel ordering, fixed frequency-axis equality, warm-up/unavailable windows, and channel-local quality failures.

## 3. Replace the topomap backend contract

- [x] 3.1 Replace `TopomapBand`/`bands` API models with typed scalar spatial fields and explicit field identities; update run and dynamic routes to require requested field IDs.
- [x] 3.2 Add the PSD adapter that selects one exact frequency-bin index from the current stored PSD result and returns the actual frequency and `V^2/Hz` unit without reading raw samples or recomputing PSD.
- [x] 3.3 Migrate the RBP adapter to explicit relative/absolute band field IDs backed only by structured RBP arrays, preserving current units and unavailable behavior.
- [x] 3.4 Verify montage identity, ordered channel matching, per-channel exclusions, insufficient-point behavior, structured errors, and no stale window/result substitution in service and API tests.

## 4. Migrate Review and WPF consumers

- [x] 4.1 Replace desktop topomap DTOs, HTTP requests, providers, and canvas bindings with the scalar `fields[]` contract; remove `bands`, `power_type`, and `band_id` readers and callers.
- [x] 4.2 Update Review PSD configuration to capture the ordered analysis channel set and montage identity; let the PSD line-chart channel selector choose from the existing Run without creating a new Run.
- [x] 4.3 Add the PSD frequency selector from the active result axis; selecting a bin requests the current window's matching field and does not alter the playback cursor or scientific Run identity.
- [x] 4.4 Update every RBP Review and standalone topomap consumer to send explicit field IDs and verify stable control identity during playback.
- [x] 4.5 Add desktop tests for exact-bin selection, channel display selection, unavailable channels, stale response rejection, field identity changes, and no Run creation on presentation-only changes.

## 5. Validate the coordinated cutover

- [x] 5.1 Run focused backend scientific, result-contract, topomap API, and RBP tests; run the desktop test suite and build.
- [x] 5.2 Measure dynamic PSD processing and persisted result size with representative 19- and 32-channel recordings, and confirm frequency selection performs only one spatial projection without delaying Review playback.
- [x] 5.3 Manually verify Review playback and seek: the selected map uses the active PSD window, exact returned frequency bin, `V^2/Hz`, and authoritative montage positions; an invalid or under-populated map shows an explicit unavailable state.
- [x] 5.4 Verify historical single-channel PSD results are not migrated, falsely mapped, or reused; confirm a new current-contract Run is requested.
- [x] 5.5 Run `openspec validate add-frequency-resolved-psd-topomap --strict`, `git diff --check`, and the project code-size review for changed business files.
