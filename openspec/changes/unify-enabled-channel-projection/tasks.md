# Tasks

## 1. Shared desktop projection

- [x] 1.1 Add the enabled EEG projection rule and focused tests for selection, configured order, native identity, missing snapshot, malformed snapshot, and duplicate mapping; verify the desktop test project passes.
- [x] 1.2 Apply the projection to review raw-channel catalog/projector and verify disabled channels are absent while selected sample values still come from original stream indexes.
- [x] 1.3 Apply the projection to impedance SDK readings and live analysis/topomap choices; verify SDK channel supersets preserve stream order while disabled channels cannot create rows or algorithm choices.

## 2. Import-time channel binding

- [x] 2.1 Add schema migration 23 and backend request/response persistence for optional import snapshot and ordered column mapping; verify migration 22→23, rollback-on-error, and metadata round-trip tests.
- [x] 2.2 Match exact one-to-one EEG labels at the backend import boundary using the optional desktop snapshot; verify exact, missing, ambiguous, duplicate, and non-EEG cases in backend tests.
- [x] 2.3 Carry persisted import metadata into the desktop recording manifest and projection; verify a subsequent configuration edit does not change imported recording channel visibility or source-order sample reads.

## 3. Integration verification

- [x] 3.1 Run OpenSpec validation, focused backend import/migration tests, focused desktop channel/review/acquisition tests, and the desktop build; record any environment-limited check and verify unrelated worktree changes remain untouched.
- [x] 3.2 Map ANT/eego compact impedance stream indexes to requested physical identities in stream order; fail safely on count mismatch and test non-contiguous native indexes, labels, and role-specific index reuse.

## 4. Audit closure

- [x] 4.1 Add the exported signal SHA-256 to the channel sidecar and pass a bounded channel-identity manifest with imports; validate its digest, EEG column order, source labels, native indexes, and immutable snapshot before persisting the mapping. Add export/import round-trip and tamper/mismatch tests.
- [x] 4.2 Keep raw EDF/BDF labels and sample-column order separate from restored display labels; update the imported recording access manifest and validation so aliases display the mapped electrode without changing backend raw sample names or values. Add desktop tests.
- [x] 4.3 Route live waveform channel selection through `EnabledEegChannelProjection`; remove duplicate index-only mapping and test label/kind mismatch and fallback behavior.
- [x] 4.4 Declare prepared imported EEG arrays as `V` while retaining original source units on the recording; test a µV-header EDF produces V-valued samples and a V prepared-source contract.
- [x] 4.5 Revalidate ANT/eego impedance identity ordering against the checked-in SDK contract and tests; record any hardware-only identity guarantee that cannot be established from SDK metadata.
- [x] 4.6 Run focused export/import, algorithm source/unit, projection, impedance tests, desktop build, and OpenSpec validation without disturbing unrelated worktree changes.
