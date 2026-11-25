# Design

## Context
Scientific data are sample-major float64 volts. QC masks use absolute half-open sample intervals. ICA/SSP cannot reconstruct a dense spatial transform from a partially missing row. Restoring raw samples as cleaned or filling zeros would fabricate scientific output.

## Goals / Non-Goals
Preserve valid MNE output and necessary missing values, explain exactly which channels and intervals become additionally unavailable, and share this evidence between preview and algorithms. Do not change artifact detections, reviewed ICA fitting, references, filters or raw files.

## Decisions
A pure scientific validity helper compares input/output finite masks and run-length encodes constant missing-channel sets. Each interval records missing input channels, unavailable output channels, additionally unavailable channels and an explicit dependency reason. Counts distinguish time samples from channel-samples. ICA, SSP and EOG regression share this result evidence. A common projection clips evidence to requested absolute windows and output channels. Detector segments are never rewritten. Preview uses an explicit response model; desktop displays gap ranges and missing inputs in its existing status area. Spectrum success and failure evidence carry the same validity projection; numerical quality gates remain unchanged. Application version enters active removal specifications/identities, while ICA-fit identity stays unchanged because fitting does not change.

## Risks / Trade-offs
ICA gaps remain scientifically necessary. Highly fragmented missing masks can produce many intervals; encoding avoids a samples-by-channels JSON mask. No automatic interpolation or untreated-data fallback is introduced. Empty ICA exclusion preserves the original mask exactly. New helper has a single scientific responsibility; existing removal module retains its registered size assessment, updated if needed.

## Migration / Rollback / Validation
No persisted patient data migrate. Application evidence version prevents old prepared/algorithm results from being silently reused. Roll back implementation and evidence version together; unchanged fitted ICA models remain usable. Add missing-mask/no-op/boundary tests, typed preview and desktop status tests, spectral failure evidence tests, then verify the user's recording locally under ignored `.artifacts` with independent MNE comparison and source hashes.

The requirement has been synchronized to the current main specification during implementation. The retained delta uses MODIFIED so later archive can update that synchronized requirement idempotently.
