## ADDED Requirements

### Requirement: Save action reflects unsaved channel configuration changes

The desktop SHALL show the channel configuration save action only when an
editable draft differs from its saved or initialized baseline. A successful
save SHALL update the baseline and keep the draft open for continued editing.

#### Scenario: Opening an unchanged configuration
- **WHEN** an operator opens an editable saved channel configuration
- **THEN** the save action SHALL remain hidden until a persisted field changes

#### Scenario: Saving and editing again
- **WHEN** an operator changes a draft and saves it successfully
- **THEN** the save action SHALL be hidden
- **AND** a subsequent change SHALL show the save action again

#### Scenario: Restoring the saved value
- **WHEN** an operator changes a field and restores its saved value
- **THEN** the draft SHALL no longer be considered changed if all persisted fields match the baseline

### Requirement: Leaving a modified channel configuration requires a choice

The desktop SHALL ask whether to save when an operator leaves a channel
configuration draft with unsaved changes. Choosing Yes SHALL save before
navigation and keep the editor open if saving fails. Choosing No SHALL discard
the draft and leave the page. Leaving an unchanged draft SHALL not prompt.

#### Scenario: Save while leaving
- **WHEN** an operator chooses Yes in the unsaved-changes prompt
- **THEN** the configuration SHALL be saved before navigation
- **AND** navigation SHALL occur only after save succeeds

#### Scenario: Discard while leaving
- **WHEN** an operator chooses No in the unsaved-changes prompt
- **THEN** the draft SHALL be discarded and navigation SHALL continue
