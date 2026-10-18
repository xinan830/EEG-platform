## ADDED Requirements

### Requirement: Render official RBP in its own WPF detail view

The desktop SHALL display the backend-returned Delta, Theta, Alpha, Beta, and
Gamma relative powers in a dedicated RBP result view for current
`official-rbp-v2` Runs. Historical four-band Runs SHALL retain their original
four-band labeling. The view SHALL identify the source window and ratio unit,
and SHALL NOT recompute, renormalize, or substitute missing values with zero.

#### Scenario: Static RBP result

- **WHEN** a completed current static RBP Run contains five clean band values
- **THEN** WPF displays those values in the declared band order
- **AND** the shared quality and provenance controls remain available

#### Scenario: Static quality failure

- **WHEN** RBP returns a completed Run whose quality gate rejects the result
- **THEN** no numeric share is plotted
- **AND** WPF shows the backend failure reason

### Requirement: Synchronize dynamic RBP with the WPF time cursor

The desktop SHALL use each backend RBP window's end time, analysis state,
quality, and five band values when rendering the current dynamic trend and
window. Seeking or playing the shared time cursor SHALL update both views.

#### Scenario: Rejected dynamic window

- **WHEN** the time cursor reaches a rejected or unavailable RBP window
- **THEN** the current window shows unavailable shares and the trend contains
  a gap at that window
- **AND** no previous-window share is presented as the current measurement

#### Scenario: Cursor before first window

- **WHEN** the cursor is earlier than the first released RBP window
- **THEN** the result view shows no current-window measurement
