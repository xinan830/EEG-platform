## ADDED Requirements

### Requirement: Review navigation does not restore obsolete targets

Review SHALL preserve the latest requested playback position and derived viewport
when source preparation or older frame loading reports progress. A newer cached
target SHALL supersede older outstanding foreground work just as an uncached
target does. Only complete, current-identity frames MAY be published.

#### Scenario: Preparation reports progress during a newer drag
- **WHEN** a user changes the thumb target while an older load is pending
- **THEN** progress and completion notifications SHALL NOT restore the older
  requested viewport or position.

#### Scenario: A cached target supersedes an unfinished cold load
- **WHEN** navigation returns to a cached viewport before a cold target finishes
- **THEN** that cached frame SHALL remain displayed
- **AND** the old load SHALL NOT subsequently publish its frame.

### Requirement: Review thumb dragging preserves pointer and clock ownership

The navigator SHALL follow pointer input across the full recording without
programmatically repositioning the operating-system pointer. Waveform work SHALL
remain coalesced and bounded independently of pointer speed. The single playback
clock SHALL pause during a thumb drag and restore its previous running state at
drag completion or capture loss.

#### Scenario: The user drags in either direction while playback is running
- **WHEN** the user starts and then completes a forward or backward thumb drag
- **THEN** the mouse SHALL NOT be moved by the application
- **AND** playback ticks SHALL NOT move the target during the drag
- **AND** playback SHALL resume from the final target after the drag.

### Requirement: Review reuses immutable trace data and actual plot coordinates

Status/progress notifications, annotation changes, playback ticks and unchanged
visible-duration notifications SHALL NOT rebuild identical trace arrays.
The playhead and annotations SHALL use the actual displayed axis coordinates,
including while an uncached target is loading. Numeric display projection SHALL
preserve values, units, temporal extrema and genuine missing-data breaks.

#### Scenario: A target is not covered by the displayed frame
- **WHEN** a new target frame is loading
- **THEN** the chart axis SHALL immediately follow the requested viewport
- **AND** the playhead SHALL remain visible at the requested position
- **AND** the full source-cache range SHALL NOT replace the requested coordinate system.
- **AND** the complete target frame SHALL be published atomically when ready.

#### Scenario: Only status, cursor or annotations change
- **WHEN** the immutable frame and physical vertical scale are unchanged
- **THEN** SciChart trace data SHALL remain unchanged
- **AND** a changed frame or physical scale SHALL still refresh the waveform.
