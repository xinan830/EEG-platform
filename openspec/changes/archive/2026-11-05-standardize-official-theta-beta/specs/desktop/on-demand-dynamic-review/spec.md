## ADDED Requirements

### Requirement: Review selects and submits the Theta/Beta frequency mode
The recording Review Theta/Beta panel SHALL expose Standard and IAF-relative modes, default to Standard, and submit the selected mode with each on-demand scientific configuration. The panel SHALL display backend-owned results and SHALL NOT calculate frequency bands or the ratio in WPF. Its dynamic policy SHALL use the catalog-declared 20-second default window and one-second refresh step without numeric warm-up values.

#### Scenario: Change mode and recompute the current Review window
- **WHEN** the user changes the Theta/Beta mode while the Review panel is open
- **THEN** the active configuration is invalidated and the current cursor window is recomputed with the selected mode
- **AND** the request carries that mode to the backend

#### Scenario: Wait for a complete Review window
- **WHEN** the playback cursor is before the first complete 20-second TBR window
- **THEN** Review shows the window as insufficient or pending
- **AND** it does not display a partial numeric ratio
