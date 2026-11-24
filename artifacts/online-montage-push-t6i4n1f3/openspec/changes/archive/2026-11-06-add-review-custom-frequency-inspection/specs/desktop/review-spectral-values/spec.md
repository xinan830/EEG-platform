## ADDED Requirements

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
- **THEN** the numeric-panel owner completes and publishes that pair, retains only the newest pending target for both sources, and never waits on the waveform clock
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
