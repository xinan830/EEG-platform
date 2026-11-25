# Design

## Root cause and scope

The same thresholds do not define the same detection when filters, time context and channel population differ. A 149.982 s real recording reproduced list-only PTP intervals at 148.025–149.100 and 149.382–149.982 s.

## Decisions

1. Store the immutable ArtifactSegmentReport alongside the existing annotated continuous prepared source. It covers the complete effective montage/channel population before artifact removal.
2. Build reject/removal sources from that annotated source using the existing mapped-array owner. Copy samples without refiltering; apply report intervals exactly by half-open sample range and affected channels, then perform configured removal. No extra detector cache or copied scientific formulas.
3. Inspection calls the same facade and projects its report to the requested absolute range. Default scientific filters match Review (1–50 Hz plus 50 Hz notch). Report cropping cannot alter detection context.
4. Bounded STFT/TNK filtering keeps its finite-context numerical contract. With QC enabled, supply the recording context and gate using the continuous report projected onto the requested channels/range, never refitting muscle z-scores on a single channel/window. The cold full QC source is disk-backed and shared; QC-disabled bounded I/O remains unchanged. Missing recording context fails explicitly.
5. Threshold/bad-channel/montage changes clear stale desktop lists and markers. Unchanged rescans preserve confirmed ICA. Policy/removal changes reuse detections.

## Contracts and risks

Internal samples remain sample-major float64 volts, API waveform units remain microvolts, intervals are recording-absolute half-open sample bounds. Channel order, source gaps and original files remain unchanged. Scan range limits bound returned coverage, not the underlying detection context; expose this in evidence. Invalid parameters/manual channels fail explicitly. The canonical cache identity includes input, montage, filter and QC specification; bump the artifact detection semantic version and ICA fit identity to reject old confirmations.

## Verification and rollback

First reproduce mismatched scan/mask on deterministic data, then assert exact per-channel NaN masks, annotate/reject report reuse, bounded single-channel projection, range/filter/cache isolation, stale UI invalidation and unchanged QC-disabled output. Re-run the user's real recording locally without storing patient data in tracked files. Run related backend/desktop tests, architecture, size and whitespace checks. Roll back touched producer/consumer files together; no migration is required.

## Size review

Keep new report projection in the scientific artifact-report responsibility and reuse existing services. Register any touched business file exceeding 400 physical lines with a concrete responsibility assessment.
