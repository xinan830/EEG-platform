# POP presentation verification — 2026-11-26

## Scope and result

Application-owned device discovery/stream names, setup/connection errors, channel and montage configuration UI, acquisition/review montage selectors and TNK tab labels use POP. Product brand belongs to DeviceProductPresentation; default channel identity and preset presentation stay in their existing domain owners. No aliases, name-prefix classification, snapshot migration or new dependency were introduced.

Historical system profiles render through JsonIgnore display properties. Saved names/descriptions, stable IDs, snapshot hashes, hardware model/serial facts, SDK ABI/library filenames, stable error codes and vendor provenance stay intact. User-source profiles display their exact names, including ANT-containing names. Historical records whose profiles declare User source are treated as user-owned; names alone are never used to relabel or suppress them. New system profiles store POP text. EEG values, channel order, formulas, clocks, API/WebSocket contracts and scientific processing were not changed.

System semantic deduplication now uses declared source, default channel identity, actual channel-mapping fingerprint and ordered reference formulas. Review snapshot equivalence uses the same identity rather than profile names; exact recorded snapshots remain selected.

## Checks

- Desktop Debug build including WPF/BAML completed using `.artifacts/pop-dotnet`, because the running desktop process locked the ordinary executable output. The running application was not stopped.
- Focused desktop verification: **517 passed, 0 failed** after explicitly excluding the three pre-existing/scheduling cases below. Includes four new branding contract tests plus Devices, Channels, Montages, Acquisition, TNK workbench and selected recording/review integration tests. TRX: `.artifacts/pop-test-results/pop-focused-clean.trx`.
- Unfiltered selected desktop group: **517 passed / 3 failed / 520 total**. This is not an all-green full-suite claim. TRX: `.artifacts/pop-test-results/pop-verification.trx`.
- Clean HEAD `46c38109cbb3de8094094d78ea8530333b915577` sources were extracted into an ignored evidence directory without changing the working tree. The same base module selection returned **507 passed / 2 failed / 509 total**; the two failures match current failures below. TRX: `.artifacts/pop-baseline-results/baseline-modules.trx`.
- Backend architecture checks: **2 passed / 22 deselected**, for scientific service boundary and direct eeg_core import ownership. No backend business files changed.
- New tests verify generated POP names, historical JSON/signal fingerprint preservation, user-name preservation, mapping/formula-sensitive deduplication, historical system IDs, native driver/error identity and POP readiness.
- Production scan found one non-comment uppercase ANT literal outside stable error codes: the intentionally retained `hardwareConfiguration["vendor"] = "ANT/eego"`. Native lowercase identifiers and algorithm/vendor source comments are intentionally retained.
- Scoped physical-line check: **38 touched C#/XAML files assessed, 0 policy failures**. Existing channel editor over 1000 lines received an explicit split plan; branding does not add state or grow that file. Existing oversized test fixtures are registered. New presentation files are below 400 lines.
- `openspec validate present-pop-device-branding --strict` and `git diff --check` passed.

## Existing failures and limits

1. `ConfigureChannels_DoesNotAutoDisplayUnnamedPhysicalInputs`: expects Fp1, receives no visible channels. Reproduces on clean HEAD.
2. `EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest`: rejects a recorded montage identity as different from the reference index. Reproduces on clean HEAD.
3. `SingleInstanceGuardBlocksDuplicateAndAllowsReacquireAfterDispose`: intermittently sees a second mutex guard; passed in one current module run and in clean HEAD runs, failed in other current runs. The test uses Task.Run to stand in for another mutex-owning thread; the mutex code and test were not changed. This remains a scheduling-sensitive failure, not a verified all-green area.

No full desktop/backend suite, physical amplifier/impedance session, or manual inspection of every screen was performed. Unrelated preprocessing UI work in the shared working tree was preserved and was not incorporated into the branding implementation. The POP-only staged source snapshot was independently rebuilt and passed the same 517 focused desktop tests, excluding the three cases listed above. Evidence: `.artifacts/pop-staged-results/pop-staged.trx`. Unrelated local preprocessing UI and guide edits are excluded from the POP commit.
