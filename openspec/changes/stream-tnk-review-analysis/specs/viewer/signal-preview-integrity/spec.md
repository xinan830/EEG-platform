## Purpose

Define performance observability and transient-preserving display summaries for the recording signal-preview API without changing its scientific preprocessing or legacy uniformly sampled fields.

## ADDED Requirements

### Requirement: Preserve per-channel preview extrema
When the preview reduces source samples for display, the backend SHALL return each requested channel's minimum and maximum value for every display bin as additive metadata. The desktop SHALL render this envelope with the existing preview line. The existing `elapsed_s`, `channels`, units, channel order, and filter semantics SHALL remain compatible. Envelope values SHALL be in µV, and envelope times SHALL be recording-relative bin centers; they SHALL be identified as display summaries rather than exact event times or analysis results.

#### Scenario: A narrow transient falls between uniformly sampled preview points
- **WHEN** a source-channel minimum or maximum occurs inside a display bin but is not selected by the legacy uniform stride
- **THEN** the response's channel envelope contains that minimum and maximum in µV
- **AND** the desktop renders the bin range while retaining the legacy preview line and recording time axis

#### Scenario: No display reduction is needed
- **WHEN** the requested preview window contains no more source samples than `max_points`
- **THEN** the desktop displays the original samples and the response omits the optional extrema envelope

### Requirement: Diagnose signal-preview stages
When `BRAIN_REVIEW_PERF_DIAGNOSTICS=1`, the backend SHALL emit metadata-only signal-preview timings joined by request ID and recording ID. The record SHALL separate continuous-source preparation, window slicing/validation, and display projection, and SHALL include channel count, source sample count, displayed point count, stride, envelope-bin count, and total request time from the existing HTTP request log. Diagnostics SHALL NOT include EEG arrays or user parameter payloads.

#### Scenario: Compare warm and cold preview requests
- **WHEN** two requests use the same recording and preprocessing contract, one after another
- **THEN** their request-correlated diagnostics show preparation and projection durations separately
- **AND** existing prepared-source cache-miss records can be compared by recording identity without changing the response's time, unit, channel, or filter contract
