# Design

## Context
Raw WPF readers expose only EEG; mounted readers hide auxiliary source channels. Existing artifact contracts already hold EOG/ECG names but desktop omits them.

## Goals / Non-Goals
Read explicitly selected voltage channels by immutable raw source identity on the same sample clock. Do not add them to EEG references, interpolation donors, topomaps or spectral output. No independent frontend science.

## Decisions
Use existing source sessions with an explicit raw-voltage reading mode and a focused reference service. Discover names without sample loading; reject missing/ambiguous/non-voltage names and EOG/ECG overlap. Allocate auxiliary arrays through existing preparation budget, filter their continuous finite spans consistently, and pass them separately to ICA/SSP/regression. ICA fits EEG only; append references as EOG/ECG only to scoring Raw. Mask scoring rows using joint finite EEG/reference input; application retains original EEG mask semantics. Explicit references are raw-source names, independent of selected EEG montage. Keep empty-selection legacy behavior. Model/preparation fingerprints bind selection and revised reference semantics.

## Alternatives
Appending references to EEG was rejected because it changes ranks, CAR, topomaps and outputs. UI-only name selection was rejected because montage/EEG filtering can hide recorded sources.

## Effective analysis layout
The desktop resolves the effective EEG channel list once at the shared analysis-entry boundary via `POST /api/recordings/{id}/preprocessing/analysis-channels`. This opens the actual execution reader with the selected montage, bad-channel policy and reference partition, without filtering or loading samples. All panel parameter lists and montage output selections consume that layout. The desktop never guesses dependencies by suffix or implements CAR rules. PSD/RBP composite panels share the same resolution. The underlying recording/montage descriptor remains immutable.

For EDF/BDF raw voltage discovery, verify physical dimensions and native sample rates from the original header using the existing pyedflib dependency; MNE alone can label non-voltage counters as volts. Mixed native voltage rates are rejected explicitly, rather than accepting MNE implicit resampling. This initial version requires a common native voltage clock; separate explicit resampling is outside scope.

ECG selection applies to ICA; EOG selection also applies to SSP/regression. Disabled or unsupported removal methods omit these roles from execution. Until ICA is confirmed, ordinary algorithms retain their existing input path. Once confirmed, references are partitioned consistently for execution and analysis lists. Changing selection clears confirmation, preview and artifact report. Reloading unchanged choices retains confirmation and the WPF selected-item display.

## Migration / Rollback
No schema migration; deploy producer and consumer together. Existing workflows keep empty-selection behavior. New semantics version invalidates stale ICA/preparation cache identities. Revert both sides and reopen recording to roll back.

## Risks / Validation
Synthetic WPF and EDF auxiliary channels, non-voltage/missing/overlap failures, exact sample alignment and EEG-only output, deterministic scoring/model reuse, configuration and request propagation, stale-response cancellation. Existing dirty waveform overlay files are outside scope. Business files target 400 lines; register justified exceptions. Real recording lacks dedicated EOG/ECG, so dedicated-electrode hardware/real physiological acceptance remains manual.
