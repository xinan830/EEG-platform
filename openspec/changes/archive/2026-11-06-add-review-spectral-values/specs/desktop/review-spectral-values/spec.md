## Purpose

Let review users inspect concrete numeric values from the existing PSD and RBP analysis paths for each montage output and the current playback analysis window.

## ADDED Requirements

### Requirement: Display selected-band values from existing review results
The review workspace SHALL provide a button that opens a right-side numeric panel. The panel SHALL provide a selector for the canonical Delta (1–4 Hz), Theta (4–8 Hz), Alpha (8–13 Hz), Beta (13–30 Hz), and Gamma (30–50 Hz) bands and SHALL show one row per montage output in the current montage order. For the selected band, each row SHALL display amplitude in µV, RBP absolute power in µV², RBP relative power as a percentage, and PSD band-mean density in µV²/Hz. PSD and RBP values SHALL come from their existing current review results and backend projections; the panel SHALL use the existing PSD and RBP dynamic adapters at the same playback cursor, and SHALL NOT add a scientific algorithm or display a chart in place of numeric values.

#### Scenario: Inspect a valid selected band
- **WHEN** the user opens the panel and selects a band for which current PSD and RBP results are available
- **THEN** the panel displays one numeric row per montage output with the four values, units, selected band, and source quality for the current analysis window

#### Scenario: Change the selected band
- **WHEN** the user selects another canonical band
- **THEN** the panel displays values from that same current PSD/RBP result pair for the newly selected band without rerunning either result's analysis window

#### Scenario: Inspect a row's source details
- **WHEN** the user hovers a numeric row
- **THEN** a bounded, wrapping tooltip shows the channel and selected band, readable PSD/RBP quality status, supplied segment counts and peak amplitude, values in the table's units to six significant digits, and any unavailable reason/code without rendering serialized quality or failure objects

#### Scenario: Hover the adjacent RBP stacked history
- **WHEN** the user hovers a channel's stacked history in the review RBP panel
- **THEN** its container does not show the aggregate quality-warning/unavailable-window tooltip; chart point inspection and existing quality data remain unchanged

### Requirement: Separate scalar reads from spatial rendering and retain table rows
Numeric reads SHALL consume a typed scalar-only resource for the saved analysis window. Map and numeric consumers SHALL share the same result-to-scalar implementation, and numeric reads SHALL NOT resolve electrode coordinates, interpolate a grid or generate chart payloads. The table SHALL retain channel row objects and UI containers across window updates, band changes and unavailable transitions while channel order is unchanged. Waveform seek and playback ticks SHALL NOT await scalar completion. No topomap fallback or alternate PSD/RBP computation SHALL be introduced.

#### Scenario: Refresh a numeric window
- **WHEN** the panel reads five-band PSD and amplitude scalars from saved windows
- **THEN** values match the shared map input projection, no spatial preparation or interpolation runs, and only changed properties on existing table rows notify the UI

#### Scenario: Scalar loading is delayed
- **WHEN** an outstanding scalar response is delayed and the user seeks or playback advances
- **THEN** waveform navigation and the shared clock continue independently, and the late scalar response cannot restore an older numeric window

### Requirement: Preserve the existing playback analysis window and source identity
The panel SHALL use the existing playback analysis-window settings and segmentation behavior of the PSD and RBP paths. A displayed PSD/RBP row SHALL refer to the same recording, actual analysis window, ordered montage outputs, and compatible analysis settings. The panel SHALL preserve each source's declared unit, quality, and provenance and SHALL NOT present results from different windows as a matched pair.

#### Scenario: Inspect values after playback advances
- **WHEN** the current playback analysis window changes
- **THEN** the panel updates from the existing PSD and RBP results for that window and does not continue presenting values from the previous window as current

#### Scenario: Source windows do not match
- **WHEN** PSD and RBP results are missing or their recording, actual window, montage order, or analysis settings do not match
- **THEN** the panel marks affected values unavailable with a reason and does not combine them into a valid-looking row

### Requirement: Derive only RMS amplitude from RBP absolute power
The panel SHALL obtain amplitude from the existing RBP result projection, which SHALL apply the backend RMS helper to the matching RBP absolute power in µV², yielding RMS amplitude in µV. This projection SHALL NOT change official RBP result arrays or quality rules. The desktop SHALL NOT calculate amplitude or derive PSD/RBP values. Missing, non-finite, or negative absolute power SHALL produce an unavailable amplitude with a reason rather than zero.

#### Scenario: RBP absolute power is available
- **WHEN** a montage output has a valid nonnegative selected-band RBP absolute power
- **THEN** the panel displays its backend-projected RMS amplitude in µV

#### Scenario: RBP absolute power is unavailable
- **WHEN** the selected-band absolute power is missing or invalid for a montage output
- **THEN** its amplitude is unavailable with the source reason and is not represented as zero
