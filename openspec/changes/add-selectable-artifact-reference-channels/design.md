# Design

## Context
At the original change baseline WPF analysis readers exposed hardware EEG, and mounted readers hid auxiliary sources. Scoring selection also implicitly removed selected EEG channels. This extension separates persisted physiological identity from scoring choice across readers, preview and ordinary algorithms.

## Goals / Non-Goals
Read explicitly selected voltage channels by immutable raw source identity on the same sample clock. Dedicated EOG/ECG and unconfirmed sources stay outside EEG references, interpolation donors, topomaps and spectral output. EEG proxies retain EEG membership. No independent frontend science.

## Decisions
Use existing source sessions with an explicit raw-voltage reading mode and a focused reference service. Discover names without sample loading; reject missing/ambiguous/non-voltage names and EOG/ECG overlap. Allocate auxiliary arrays through existing preparation budget, filter their continuous finite spans consistently, and pass them separately to ICA/SSP/regression. ICA fits EEG only; append references as EOG/ECG only to scoring Raw. Mask scoring rows using joint finite EEG/reference input; application retains original EEG mask semantics. Explicit references are raw-source names, independent of selected EEG montage. Keep empty-selection legacy behavior. Model/preparation fingerprints bind selection and revised reference semantics.

## Alternatives
Appending references to EEG was rejected because it changes ranks, CAR, topomaps and outputs. UI-only name selection was rejected because montage/EEG filtering can hide recorded sources.

## Effective analysis layout
The desktop resolves the effective EEG channel list once at the shared analysis-entry boundary via `POST /api/recordings/{id}/preprocessing/analysis-channels`. This opens the actual execution reader with the selected montage, bad-channel policy and reference partition, without filtering or loading samples. All panel parameter lists and montage output selections consume that layout. The desktop never guesses dependencies by suffix or implements CAR rules. PSD/RBP composite panels share the same resolution. The underlying recording/montage descriptor remains immutable.

For EDF/BDF raw voltage discovery, verify physical dimensions and native sample rates from the original header using the existing pyedflib dependency; MNE alone can label non-voltage counters as volts. Mixed native voltage rates are rejected explicitly, rather than accepting MNE implicit resampling. This initial version requires a common native voltage clock; separate explicit resampling is outside scope.

ECG selection applies to ICA; EOG selection also applies to SSP/regression. Disabled or unsupported removal methods omit scoring choices from execution. Persisted identities always determine EEG membership, independently of removal state or ICA confirmation. Unconfirmed ICA leaves removal inactive but never changes that partition. EEG proxies remain EEG before and after confirmation. Changing selection clears confirmation, preview and artifact report. Reloading unchanged choices retains confirmation and the WPF selected-item display.

## Reference role consistency fixes
The source partition boundary removes non-EEG raw labels and their verified canonical aliases from the effective EEG bad-channel specification; selecting an EEG proxy does not remove its bad-channel policy. Unrelated unknown bad channels still fail validation. All four WPF/EDF raw/montage callers consume this effective specification; auxiliary samples remain separately readable.

Bad-segment inspection consumes the same effective payload as scientific preview and supported ordinary algorithms. Confirmed identities determine the same EEG population before and after ICA confirmation, while scoring/removal follows the effective payload. Changing removal method or transitioning between confirmed/unconfirmed ICA with references invalidates the report. If the first scan enables rejection and invalidates confirmed ICA, discard any response whose effective configuration changed and ask for a fresh scan.

Role conflicts invalidate and cancel stale work through the existing configuration event, but do not schedule new preview or analysis work. The shared payload boundary rejects conflicting selections even when a method would otherwise drop ECG. Resolving the conflict restores normal preparation. Excluded badges consume verified backend excluded-output lists independently of the bad-channel policy. The ECG selector reuses the shared rounded control and renders the empty transport sentinel as an explicit automatic-selection label.

## Separate physiological identity from reference selection
Each recorded raw voltage source has a persisted user-confirmed identity: EEG, dedicated EOG, dedicated ECG, or unconfirmed. Hardware acquisition kinds remain unchanged. Reference selection never changes identity. EEG sources selected for eye scoring remain EEG fitting/montage inputs; only dedicated auxiliary identities are partitioned out of EEG, including when removal is disabled. Automatic frontal selection and explicit selection of the same EEG proxies use the same already-prepared EEG signals, filter, reference and scoring path. An unknown source identity must be confirmed before explicit reference use; legacy source EEG membership is shown transparently as recorded EEG metadata, never inferred solely from Fp/EOG/ECG names.

Identity overrides are stored as atomic source-hash-bound metadata beside managed recording storage, leaving acquired samples and manifests immutable and avoiding a database schema migration. Export includes the resolved identity in each source-column mapping; import validates and restores it. Source identity fingerprints include role configuration so existing ICA/preparation results cannot survive a role change. Review controls save identity before allowing affected requests and cancel stale work while saving. EOG regression only accepts dedicated EOG; EEG proxy scoring is supported for ICA/SSP. Real dedicated-electrode accuracy remains outside synthetic acceptance.

## Migration / Rollback
No database schema migration; deploy producer and consumer together. Empty choices resolve declared EOG/ECG first and optionally EEG frontal proxies; names alone never create dedicated identities. Saved role overrides must accompany managed recording storage and exported labels metadata. New semantics invalidate stale ICA/preparation identities; reopen and refit after rollback.

## Risks / Validation
Synthetic WPF and EDF auxiliary channels, non-voltage/missing/overlap failures, exact sample alignment and EEG-only output, deterministic scoring/model reuse, configuration and request propagation, stale-response cancellation. Ordinary colored waveform samples and display filtering remain separate. Scientific comparison admits missing display rows only when the backend explicitly declares them excluded, with a badge and no invented trace. Business files target 400 lines; register justified exceptions. Real recording lacks dedicated EOG/ECG, so dedicated-electrode hardware/real physiological acceptance remains manual.

## Identity lifecycle and size review
Managed identity metadata is atomically replaced, source-hash-bound and schema/column/voltage validated. Identity changes invalidate prepared sources and reviewed ICA. Source sessions fence changes, continuous preparation checks its signature before returning, queued Runs reject changed signal mappings, and dynamic workers validate the frozen role signature at cancellation/publication boundaries. This targets the existing local single-user service, not a distributed multi-process transaction system.

Export retains all raw V sources including auxiliary channels and their per-column identities on the original sample clock. Import restores identities independently of hardware kind and canonical electrode aliases. Promoting an auxiliary voltage source to EEG survives the roundtrip without enabling disabled hardware EEG channels.

RecordingService remains a 634-line high-risk facade because this extension adds forwarding hooks only; persistence and identity validation live in recording_channel_roles. Catalog/registration persistence is the next extraction boundary before another use case. Other touched files above 400 lines retain lifecycle/transaction ownership under explicit caps and reassessment boundaries in docs/code-size-policy.json.
