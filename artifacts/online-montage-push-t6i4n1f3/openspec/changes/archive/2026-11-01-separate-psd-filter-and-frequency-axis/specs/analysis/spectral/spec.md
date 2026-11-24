## ADDED Requirements

### Requirement: Separate spectral preprocessing and output frequency ranges
The spectral loading boundary MUST accept independent filter and output
frequency ranges. The preprocessing cache identity MUST include the filter
range and notch setting, while output clipping MUST use only the output range.

#### Scenario: Dynamic PSD uses configured filtering with a fixed trend axis
- **GIVEN** a dynamic PSD config with high-pass 0.5 Hz, low-pass 40 Hz, and
  notch 50 Hz
- **WHEN** a dynamic window is loaded
- **THEN** preprocessing uses 0.5-40 Hz and 50 Hz notch
- **AND** returned frequency bins are clipped to the configured trend axis
  1-50 Hz
- **AND** evidence records both filter and output ranges
