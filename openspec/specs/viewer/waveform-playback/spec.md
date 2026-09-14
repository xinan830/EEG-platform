# Waveform playback baseline

## Purpose

Define the current absolute-time, fixed-page, sweep/overwrite waveform playback contract independently of scientific analysis.

## Requirements
### Requirement: Use absolute recording time

Playback position, seek input, event times, window start, and returned elapsed samples SHALL use seconds from the start of the recording.

#### Scenario: Seek to a recording position

- **WHEN** the user seeks to `125.0 s`
- **THEN** playback resumes from the corresponding source samples and does not add the current view start a second time

### Requirement: Use fixed playback pages

For a screen duration `D`, the active playback page SHALL start at `floor(current_time / D) * D`, bounded to the recording's final page.

#### Scenario: Cross a ten-second page boundary

- **WHEN** screen duration is 10 seconds and playback advances from `9.99 s` to `10.00 s`
- **THEN** the active page changes from `0-10 s` to `10-20 s`

### Requirement: Render overwrite sweep without falsifying data

The viewer MAY retain the previous page as a visual background while current-page samples overwrite it from left to right, separated by a narrow blank erase gap. Retained samples SHALL NOT participate in current-page timing, tooltip, measurement, export, or scientific analysis.

#### Scenario: Enter a new page during playback

- **WHEN** current-page data has filled only the left portion of the page
- **THEN** the left portion shows current source samples, the moving boundary is a blank erase gap, and any right-side residue is display-only previous-page content

### Requirement: Keep manual paging distinct from playback progression

Previous/next controls SHALL navigate fixed page boundaries. Pausing SHALL freeze the current page and playback position.

#### Scenario: Navigate while paused

- **WHEN** a user on `10-20 s` selects next page with a 10-second screen duration
- **THEN** the viewer displays `20-30 s` and does not create a sliding window

### Requirement: Keep buffer policy invisible

Worker queues and read-ahead buffers SHALL support performance but SHALL NOT redefine visible time or duplicate samples at new timestamps.

#### Scenario: Rotate an internal buffer

- **WHEN** buffered waveform storage is replaced or compacted
- **THEN** visible timestamps still map to the same source sample indices without a blank screen or fabricated continuity

### Requirement: Workbench navigation identifies core research tasks

The local workbench SHALL provide visible in-page navigation for waveform
review, spectrum analysis, time-frequency analysis, and results. Activating a
navigation item SHALL move the user to the corresponding workbench section
without changing recording data or submitting an analysis request.

#### Scenario: User selects spectrum analysis

- **WHEN** a recording is open and the user selects the spectrum navigation item
- **THEN** the workbench scrolls to the spectrum section
- **AND THEN** the waveform playback and analysis configuration remain unchanged.

### Requirement: Developer diagnostics require explicit opt-in

The workbench SHALL default to the ordinary research view. Render and transport
diagnostic output SHALL only be visible after the user explicitly enables
developer mode.

#### Scenario: User opens a recording

- **WHEN** the ordinary workbench view is shown
- **THEN** runtime debug information is not displayed
- **AND THEN** it becomes visible only after developer mode is enabled.

### Requirement: Viewer controls disclose their scope

The visible display-controls area SHALL identify itself as applying to waveform
review only and SHALL state that it does not change the offline frequency or
time-frequency analysis contract.

#### Scenario: User adjusts waveform high cut

- **WHEN** a user changes a waveform display filter
- **THEN** the interface identifies it as a viewer-only setting
- **AND THEN** it does not claim to change the offline analysis pipeline.
