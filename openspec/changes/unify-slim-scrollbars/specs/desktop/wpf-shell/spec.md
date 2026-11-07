## ADDED Requirements

### Requirement: Use shared slim scrollbars throughout the desktop
Desktop controls using application scrollbar resources SHALL use a shared slim style, occupying 6 DIP across the track with a 4 DIP rounded thumb in either orientation. Hovering or dragging SHALL darken the thumb. The style SHALL preserve scrolling, track paging and thumb dragging without changing scientific data or time-navigation controls.

#### Scenario: Scroll vertically or horizontally
- **WHEN** a table, list or scrollable panel has content overflowing its viewport
- **THEN** its visible vertical scrollbar occupies 6 DIP in width and its horizontal scrollbar occupies 6 DIP in height while spanning the available scrolling axis
- **AND** paging and dragging update the corresponding offset in the expected direction

#### Scenario: Use a previously styled list
- **WHEN** a page explicitly uses the existing SlimTableScrollBarStyle resource
- **THEN** it receives the same slim presentation without losing its scrolling behavior
