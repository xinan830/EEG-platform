## ADDED Requirements

### Requirement: Review annotations use a floating action panel

The review workspace SHALL open a blue and neutral non-modal annotation tool window on its right side without creating an event. Users SHALL be able to move and resize it while continuing work in review. It SHALL retain existing sample-coordinate timing, provenance protections, persistence and failure reporting.

#### Scenario: Open without a selected historical event
- **WHEN** the user activates annotation with no historical event selected
- **THEN** the tool window opens on the right with the marked-event history first and a clearly separated “添加新事件” section below it
- **AND** no event is created until the mark action is invoked.

#### Scenario: Manage historical annotations
- **WHEN** the user selects a historical event in the panel
- **THEN** jump remains available and edit/delete are enabled only for editable manual events
- **AND** editing exposes one recording-elapsed mark time in HH:mm:ss.fff and a note; interval durations are preserved rather than edited.
- **AND** activating edit expands the editor and highlights its action; activating it again collapses the editor and clears the highlight.

#### Scenario: Continue working without dismissing annotations
- **WHEN** the user clicks outside, interacts with the waveform or other tools, presses Escape, or deactivates the annotation window
- **THEN** the annotation window remains open with its current selection and edit state
- **AND** the review workspace remains interactive.

#### Scenario: Move and reactivate the same window
- **WHEN** the user drags the annotation title bar and later activates the annotation action again
- **THEN** the existing window is activated at its current position without creating a duplicate or dismissing it.

#### Scenario: Explicitly close annotations
- **WHEN** the user closes the annotation window using its window close action
- **THEN** the window closes without creating, deleting or saving an event
- **AND** the annotation action can reopen it.

#### Scenario: Dispose the owning review session
- **WHEN** the owning review page is destroyed, its recording context is replaced, or its host window closes
- **THEN** the annotation window is disposed so it cannot operate on a stale recording.

#### Scenario: Keep dropdown interaction inside the panel
- **WHEN** the user opens a definition or history dropdown and selects an item
- **THEN** the annotation panel remains open with the selected value.

#### Scenario: Present compact event marking controls
- **WHEN** the user opens the tool window
- **THEN** its title and toolbar entry read “事件标记”, with one compact draggable title bar and a manual close button
- **AND** unavailable event types show a readable placeholder and guidance, while empty event history shows an empty state instead of blank selection and inactive management actions.
- **AND** the compact window uses readable text and control sizing with sufficient spacing between current position, add, history, and edit sections.
- **AND** the panel omits the current review position card and prioritizes existing marked events before new-event controls.

### Requirement: Review creates point events by entered time or waveform placement
Review SHALL offer recording-elapsed HH:mm:ss.fff input and a colored waveform placement mode for new point events. Plot coordinates SHALL resolve against the displayed file time range and round to the nearest sample away from midpoint. Invalid time and coordinates SHALL be rejected without changing events. Placement SHALL pause playback and show a cancellable preview without changing data.

#### Scenario: Add by entered mark time
- **WHEN** the user enters a valid time and adds a selected enabled event type
- **THEN** one manual event is stored at that recording sample with zero duration, independent of the playback cursor.

#### Scenario: Add by waveform click
- **WHEN** the user activates waveform add, moves over the plot and clicks a valid position
- **THEN** the preview uses the selected event color and recording time, one point event is stored, and placement mode exits after successful persistence.

#### Scenario: Cancel or fail placement
- **WHEN** the user cancels, presses Escape, closes the event tool, or persistence fails
- **THEN** cancellation makes no data change and removes the preview; failure reports the reason and retains the draft for correction or retry
- **AND** the event tool itself remains open on Escape.

### Requirement: Review moves manual events without changing interval provenance
Review SHALL permit manual events to be moved using mark time input, waveform click placement or direct marker dragging. It SHALL preserve exact DurationSamples, definition snapshot, source, counters and other metadata. System and external events SHALL remain read-only. Moves SHALL reject non-finite or out-of-recording sample ranges, and pointer previews SHALL not persist until confirmation.

#### Scenario: Move an existing interval
- **WHEN** a manual interval is moved to a valid sample
- **THEN** the entire interval shifts and its original duration and provenance are preserved.

#### Scenario: Drag a manual marker
- **WHEN** the user drags a manual marker and releases inside the valid plot range
- **THEN** the event moves to the displayed preview time only on release
- **AND** Escape, loss of capture, or release outside the plot cancels without changing storage.

#### Scenario: Reject read-only or invalid moves
- **WHEN** an external event is targeted for a move or a time would exceed recording bounds
- **THEN** no event is changed and the UI displays the structured reason.
