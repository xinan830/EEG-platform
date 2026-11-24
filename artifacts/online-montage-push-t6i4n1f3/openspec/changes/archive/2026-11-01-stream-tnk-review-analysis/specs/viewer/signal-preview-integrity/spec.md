## Purpose

Define an independent, bounded display path for the recording signal-preview API and transient-preserving display summaries. Signal preview reads only the requested window and never enters algorithm scientific preprocessing.

## ADDED Requirements

### Requirement: Preserve per-channel preview extrema
When the preview reduces source samples for display, the backend SHALL return each requested channel's minimum and maximum value for every display bin as additive metadata. The desktop SHALL render this envelope with the existing preview line. The existing `elapsed_s`, `channels`, units, and channel order SHALL remain compatible. The filter field SHALL identify the display causal-IIR contract, distinct from algorithm scientific preprocessing. Envelope values SHALL be in µV, and envelope times SHALL be recording-relative bin centers; they SHALL be identified as display summaries rather than exact event times or analysis results.

#### Scenario: A narrow transient falls between uniformly sampled preview points
- **WHEN** a source-channel minimum or maximum occurs inside a display bin but is not selected by the legacy uniform stride
- **THEN** the response's channel envelope contains that minimum and maximum in µV
- **AND** the desktop renders the bin range while retaining the legacy preview line and recording time axis

#### Scenario: No display reduction is needed
- **WHEN** the requested preview window contains no more source samples than `max_points`
- **THEN** the desktop displays the original samples and the response omits the optional extrema envelope

### Requirement: Keep display access independent from algorithm preparation
The signal-preview request SHALL use the unified recording access session to read only the requested recording window, apply the display filter contract, and project display samples. It SHALL NOT call `prepare_signal_preview`, populate or wait for `AnalysisPreprocessCache`, or wait for TNK or any other algorithm computation. Algorithm endpoints SHALL retain their existing continuous scientific preprocessing contract independently.

#### Scenario: Preview runs while algorithm preparation is slow
- **WHEN** a signal-preview request and an algorithm request overlap for the same recording
- **THEN** the preview response is governed only by its bounded window read and display-filter work
- **AND** no preview request waits for algorithm scientific preparation or algorithm result publication

### Requirement: Diagnose signal-preview stages
When `BRAIN_REVIEW_PERF_DIAGNOSTICS=1`, the backend SHALL emit metadata-only signal-preview timings joined by request ID and recording ID. The record SHALL separate bounded window read/display-filter work, window validation, and display projection, and SHALL include channel count, source sample count, displayed point count, stride, envelope-bin count, and total request time from the existing HTTP request log. Diagnostics SHALL NOT include EEG arrays or user parameter payloads.

#### Scenario: Compare repeated bounded preview requests
- **WHEN** two requests use the same recording and display-filter contract, one after another
- **THEN** their request-correlated diagnostics show window-read/filter and projection durations separately
- **AND** no algorithm prepared-source cache record is required for either request
