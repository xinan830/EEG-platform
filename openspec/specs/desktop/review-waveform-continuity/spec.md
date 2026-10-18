# desktop/review-waveform-continuity Specification

## Purpose
TBD - created by archiving change fix-review-waveform-chunk-gaps. Update Purpose after archive.
## Requirements
### Requirement: Review chart time remains finite across source chunks
The review renderer SHALL use finite recording-second X coordinates in ascending
sample order. Contiguous source/filter chunks SHALL NOT create waveform gaps.
NaN Y SHALL express real gaps or unavailable signal values without replacing raw
data, units, channel mapping, sample counters or filter semantics.

#### Scenario: Adjacent filtered chunks in a centered viewport
- **WHEN** adjacent ten-second filtered chunks supply a REF trace while the playhead reaches the center and the viewport begins scrolling
- **THEN** the chart retains valid points on both sides of the join with finite ordered X and no fabricated discontinuity

#### Scenario: Real recorded gap
- **WHEN** source segments are separated by a sample-counter gap
- **THEN** the chart inserts a NaN Y break with finite X and never connects the waveform across missing samples

#### Scenario: Invalid samples inside a display bucket
- **WHEN** a source value is NaN or infinite within a decimation bucket
- **THEN** the trace preserves an unavailable interval rather than silently joining its surrounding finite values

#### Scenario: Filter parameters return to their original values
- **WHEN** the user changes a filter parameter then restores it
- **THEN** the same cached filtered values display with the same continuity without changing the recording or requiring cache deletion

