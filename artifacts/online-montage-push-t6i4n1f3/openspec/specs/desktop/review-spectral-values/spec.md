# desktop/review-spectral-values Specification

## Purpose

Let review users inspect concrete numeric values from the existing PSD and RBP analysis paths for each montage output and the current playback analysis window.

## Requirements

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

### Requirement: Inspect PSD and RBP values at a custom frequency selection

Recording Review SHALL provide a separate `自定义频段` action that opens a two-pane panel with one PSD curve on the right and a sortable, scrollable value table for all requested channels on the left. The custom panel SHALL support single-frequency and custom-range modes, preserve the current playback analysis window, and display amplitude RMS in µV, RBP absolute power in µV², RBP relative power as a percentage, and PSD density in µV²/Hz.

#### Scenario: Open custom frequency inspection
- **WHEN** the user opens `自定义频段` for a recording with a valid current PSD/RBP analysis window
- **THEN** the panel shows the PSD curve for the selected channel and one table row for every requested channel, in the result's channel order, with units and current source quality

#### Scenario: Keep canonical frequency inspection available
- **WHEN** custom frequency inspection is available in Recording Review
- **THEN** the existing five-canonical-band value panel remains available and retains its behavior

#### Scenario: Select a single frequency
- **WHEN** the user selects or drags the single-frequency cursor between two PSD frequency bins
- **THEN** the selection snaps to the nearest bin in the current PSD frequency grid, the displayed cursor label reports that actual frequency, and all channel rows show PSD at that bin plus RBP absolute/relative power and RMS for that bin's actual frequency interval

#### Scenario: Select a custom frequency range
- **WHEN** the user enters or drags either range boundary between two PSD frequency bins
- **THEN** each boundary snaps to the nearest frequency in the current PSD/RBP grid, an exact tie selects the lower frequency, and the panel reports those actual Hz boundaries
- **AND** all channel rows update from that same snapped interval, RBP absolute power is integrated over it, RBP relative power divides that power by the RBP total for 1–50 Hz, RMS is the square root of the RBP absolute power, and PSD is the interval's mean density

#### Scenario: Drag the selected range as a whole
- **WHEN** the user presses within the selected range fill and moves horizontally with the left button held
- **THEN** both boundaries translate together from their initial positions, preserving the Hz width and snapping to an available pair of actual bins, with no movement beyond the selectable support
- **AND** every boundary notification exposes the complete new pair; exact snapping ties prefer the lower pair, and nonuniform grids permit only pairs with the original Hz width
- **AND** dragging a boundary line, its label or its circular handle still adjusts that boundary independently

#### Scenario: Drag with one mouse press
- **WHEN** the user presses the single-frequency line, its label or handle, or a point within the plot
- **THEN** the same press begins dragging without requiring a prior selection click; clicking the plot also selects that frequency immediately
- **AND** release, capture loss, mode/grid/model changes, unavailable bins and unloading end the gesture; pointer movement after that cannot continue it

#### Scenario: Change the selected channel
- **WHEN** the user selects another channel row or sorts the table and selects a different row
- **THEN** the right pane shows that channel's PSD curve while the current frequency or range remains unchanged and table values remain associated with their channel names

#### Scenario: Inspect a PSD curve point
- **WHEN** the user hovers a valid point on the selected channel's PSD curve
- **THEN** the hover frequency label and PSD tooltip refer to the same nearest backend frequency bin, without interpolation, with Hz and µV²/Hz units

### Requirement: Keep custom-frequency metrics backend-owned and auditable

The selectable domain SHALL be the intersection of current PSD/RBP support within 1–50 Hz. The backend SHALL provide PSD density from the current PSD result and custom interval absolute/relative power from the current RBP calculation path. The backend SHALL use the actual frequency axis and documented boundary-aware integration, not infer a custom interval from the five canonical RBP aggregates.

#### Scenario: Project a custom interval
- **WHEN** the backend projects a custom interval from a valid current window
- **THEN** it returns PSD density and RBP power metrics for the same ordered channels and actual analysis window, with the applied frequency bounds, units, 1–50 Hz denominator, quality, and provenance
- **AND** the backend calculates the interval RMS and normalizes custom relative power by total 1–50 Hz RBP power, preserving actual bounds or snapped bin frequency and bin interval, ordered channel identity and source window identity
- **AND** the desktop only selects and presents returned values; it does not interpolate, integrate, sum bins, derive RBP metrics or calculate EEG values

#### Scenario: A selected bin or range is outside the available frequency support
- **WHEN** a point or range is outside 1–50 Hz or is not fully supported by the current PSD/RBP frequency grid
- **THEN** affected metrics are unavailable with a structured reason and the system does not extrapolate, clip silently, or substitute zero

#### Scenario: A selected range has invalid boundaries
- **WHEN** a range is non-finite, reversed, zero-width, or outside the declared selectable range
- **THEN** the backend rejects the request with a stable structured validation error and the desktop retains no valid-looking metrics for that range

### Requirement: Preserve result pairing and playback responsiveness

The custom panel SHALL use the existing PSD/RBP playback analysis policy, segmentation, filters, reference, recording, actual sample bounds, and ordered channels. Cursor movement and range adjustment SHALL project the already calculated current-window source and SHALL NOT start a new Welch window calculation for every interaction. A stale or mismatched projection SHALL NOT overwrite newer values. The existing five-band panel and topomap routes SHALL remain independently usable.

#### Scenario: PSD and RBP windows do not match
- **WHEN** the PSD and RBP sources differ in recording, actual sample bounds, channel order, or compatible analysis settings
- **THEN** the panel rejects the pair or marks affected values unavailable and does not combine them into valid-looking rows

#### Scenario: A projection response is delayed
- **WHEN** the user changes a range or playback window while a custom projection is outstanding
- **THEN** waveform playback, seek and viewport loading do not wait for custom-range projection completion and a late response cannot restore values for an older selection or window

#### Scenario: Adjust a frequency input with hover arrows
- **WHEN** the pointer enters a point or range endpoint input
- **THEN** compact increase/decrease buttons appear inside the input without shifting its layout
- **AND** clicking or holding a button steps through actual backend bins; keyboard Up/Down performs the same selection
- **AND** stepping stops at the frequency axis limits and cannot cross the other range endpoint; unavailable bins disable stepping and invalid text is not silently discarded
- **AND** selection continues through the existing PSD/RBP projection lifecycle without a new calculation route

#### Scenario: A source value is unavailable
- **WHEN** a PSD or RBP source is missing, unknown or rejected, or its quality, data or provenance makes a channel value unavailable
- **THEN** that value is displayed as unavailable with the source reason, without fabricating zero or reusing another channel/window

#### Scenario: First open at a fractional playback second
- **WHEN** the backend returns the latest valid step-aligned PSD/RBP window before the requested fractional cursor
- **THEN** the numeric panel validates and projects those matching actual windows without requiring the returned cursor to equal the requested sample
- **AND** the displayed bounds and provenance identify the actual source window

#### Scenario: One source is slower than the playback refresh step
- **WHEN** new playback targets arrive while a paired PSD/RBP source or projection is still loading
- **THEN** the numeric-panel owner completes the active pair, retains only the newest pending foreground target for both sources, and never waits on the waveform clock; future preparation SHALL remain bounded to two upcoming targets and SHALL NOT publish before its playback target
- **AND** queued preparation older than the newest pending foreground target is discarded before it starts, so missed deadlines cannot create a backlog of obsolete preparation
- **AND** waiting for a successor source/bin response preserves the last published rows and curve with their actual time and an explicit pending status, rather than repeatedly clearing them or presenting them as the newest target
- **AND** explicit seek, timeline drag, configuration change and close invalidate the displayed pair and reject obsolete responses immediately

#### Scenario: Edit the range while the next bin grid is loading
- **WHEN** a successor bin response is pending and the user changes the selected range or switches to range mode
- **THEN** the panel retains the selection and waits for that bin response before requesting its range projection
- **AND** it never applies a successor range response against the retained previous window's bin grid

#### Scenario: Reuse a historical montage catalogue during playback
- **WHEN** repeated window requests use the same recording, manifest content and saved snapshot contents
- **THEN** the backend reuses a bounded validated catalogue without opening one database connection per saved snapshot
- **AND** changing the manifest, saved snapshots or source directory invalidates that reuse, and returned values cannot mutate the stored catalogue

#### Scenario: Resolve a slow request while another window is admitted
- **WHEN** one request is resolving recording metadata and scientific configuration
- **THEN** that planning work does not hold the shared window admission gate
- **AND** capacity, duplicate-window admission, recovery identity and shutdown fencing remain enforced

#### Scenario: Cancel or replace the lease of an executing window
- **WHEN** another database connection cancels the window or invalidates its exact attempt token or expiry
- **THEN** the next cooperative checkpoint reads that committed state without a stale local cancellation cache
- **AND** the worker closes its owned state-reader connection on completion or failure and stale results cannot publish

#### Scenario: Prepare cold offline playback windows
- **WHEN** either numeric panel is playing and upcoming recording samples are available
- **THEN** the existing PSD/RBP computation and backend numeric projection routes prepare at most two upcoming step-aligned pairs without changing the current displayed sources
- **AND** at the matching playback position prepared results are adopted with the same recording, actual sample bounds, ordered channels, units, quality and provenance checks as demand-loaded results
- **AND** range results are reused only for matching selected bounds, while frequency selection uses returned bins without frontend scientific calculation

#### Scenario: Cancel future preparation
- **WHEN** playback pauses, the user seeks or drags the timeline, analysis configuration changes or the panel closes
- **THEN** outstanding preparation is cancelled and discarded, no old response can change current values, and no further future targets are scheduled until valid playback resumes

#### Scenario: Preparation misses a deadline
- **WHEN** a cold window is still computing at its playback target
- **THEN** waveform playback continues independently, the panel retains the previous actual window with explicit pending state, and the delayed result cannot be labelled as a different window
- **AND** diagnostics distinguish preparation duration, publication wait and actual window lag

### Requirement: Format custom-frequency table metrics to two decimal places
The custom-frequency table SHALL display RMS amplitude in µV, absolute power in µV², relative power in percent and PSD density in µV²/Hz with exactly two decimal places. Formatting SHALL preserve underlying precision, numeric sorting, source-window identity, channel order, quality and provenance. Unavailable values SHALL retain their unavailable marker.

#### Scenario: Display a valid metric
- **WHEN** a channel has a valid custom-frequency table metric
- **THEN** its cell displays the rounded value with exactly two decimal places, including trailing zeros, without modifying the source value

#### Scenario: Display an unavailable metric
- **WHEN** a channel metric is unavailable
- **THEN** the cell displays `—` rather than a numeric zero
