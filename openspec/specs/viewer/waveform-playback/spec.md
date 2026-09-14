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
