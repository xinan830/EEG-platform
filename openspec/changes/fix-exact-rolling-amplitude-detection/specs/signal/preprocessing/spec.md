## ADDED Requirements

### Requirement: Evaluate every sample-aligned amplitude window

Offline amplitude peak-to-peak quality control SHALL evaluate every complete sample-aligned window of its configured width, without skipping window origins. It SHALL mark the per-channel union of windows whose finite-value peak-to-peak strictly exceeds the configured threshold, then apply the declared minimum duration and padding. Non-finite samples SHALL remain unavailable, channel masks SHALL remain independent, and intervals SHALL retain absolute half-open sample coordinates.

#### Scenario: Extrema straddle the former stride grid

- **WHEN** a valid half-second window exceeds 300 uV but neighboring 125 ms-spaced windows do not
- **THEN** the affected channel is still detected, regardless of its offset relative to that former grid

#### Scenario: Short or boundary windows

- **WHEN** a recording is shorter than the configured width or an exceeded window touches either recording edge
- **THEN** QC evaluates the available whole short recording or the complete edge-aligned window without fabricating outside samples

#### Scenario: Revise detection semantics

- **WHEN** the exact rolling detector replaces the skipped-origin detector
- **THEN** revised QC identities invalidate reuse of older prepared signals, algorithm results and ICA confirmations, and inspection, preview and bounded gates continue to share the revised authoritative report
