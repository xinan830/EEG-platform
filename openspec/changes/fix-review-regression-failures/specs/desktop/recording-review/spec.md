## MODIFIED Requirements

### Requirement: Review thumb dragging preserves pointer and clock ownership

The navigator SHALL follow pointer input across the full recording without
programmatically repositioning the operating-system pointer. Waveform work SHALL
remain coalesced and bounded independently of pointer speed. The single playback
clock SHALL pause during a thumb drag and restore its previous running state at
drag completion or capture loss. Duplicate drag notifications SHALL NOT overwrite
the saved state or resume playback twice. Disposed workspaces SHALL NOT resume.

#### Scenario: The user drags in either direction while playback is running
- **WHEN** the user starts and then completes a forward or backward thumb drag
- **THEN** the mouse SHALL NOT be moved by the application
- **AND** playback ticks SHALL NOT move the target during the drag
- **AND** playback SHALL resume from the final target after the drag.

#### Scenario: A paused user drags with duplicate notifications
- **WHEN** a paused workspace receives repeated drag-start and drag-complete notifications
- **THEN** its selected absolute position SHALL remain stable during dragging
- **AND** it SHALL remain paused afterwards.
