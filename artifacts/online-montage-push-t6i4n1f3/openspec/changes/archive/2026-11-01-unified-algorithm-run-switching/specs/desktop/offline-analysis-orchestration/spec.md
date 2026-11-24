## ADDED Requirements

### Requirement: Preserve explicitly requested combined background work during preview changes

The shared execution coordinator SHALL distinguish an obsolete foreground execution from the background full Run explicitly started by combined mode. Moving the cursor or replacing a preview target SHALL cancel or supersede only the old preview target and SHALL NOT cancel the combined background Run. Changing the scientific configuration or leaving combined mode SHALL invalidate and cancel that background Run using its existing lifecycle.

#### Scenario: Move the preview cursor during combined execution

- **WHEN** a combined-mode background full Run is active and the user moves the preview cursor
- **THEN** the current preview target is replaced according to the on-demand target coordinator policy
- **AND** the background full Run continues.

#### Scenario: Leave combined mode or change its scientific identity

- **WHEN** the user changes the algorithm, recording, or scientific configuration so the combined background Run no longer matches the selected identity
- **THEN** the old background Run is cancelled through its existing owner
- **AND** its progress cannot overwrite the latest configuration's display.
