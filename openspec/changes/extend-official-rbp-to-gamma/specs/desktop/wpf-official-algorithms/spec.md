## MODIFIED Requirements

### Requirement: Render official RBP in its own WPF detail view

The desktop SHALL display current official RBP (`official-rbp-v2`) as five backend-returned Delta, Theta, Alpha, Beta and Gamma shares over 1–50 Hz in its RBP-owned view. It SHALL NOT compute band integrals, normalize values or replace missing shares with zero. Historical four-band Runs SHALL retain their four-band 1–30 Hz labeling.

#### Scenario: Current five-band static result

- **WHEN** the backend returns a completed current-version static RBP Run
- **THEN** WPF displays five band values and their declared ranges with shared quality and provenance surfaces

### Requirement: Synchronize dynamic RBP with the WPF time cursor

The desktop SHALL display all five current-version backend RBP shares for the selected dynamic window and five trend series. Rejected and unavailable windows SHALL remain gaps.

#### Scenario: Rejected dynamic window

- **WHEN** the cursor reaches a rejected or unavailable current-version RBP window
- **THEN** five current-window shares are unavailable and the trends contain a gap without borrowing a previous value
