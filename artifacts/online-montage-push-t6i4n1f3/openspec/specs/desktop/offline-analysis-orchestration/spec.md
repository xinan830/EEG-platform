# desktop/offline-analysis-orchestration Specification

## Purpose
TBD - created by archiving change add-background-full-analysis-orchestration. Update Purpose after archive.
## Requirements
### Requirement: Offer explicit combined offline strategy

For a dynamic algorithm with approved on-demand capability, the WPF client SHALL offer “full only”, “on-demand only”, and “on-demand preview plus background full” as orchestration strategies. The existing full strategy SHALL remain the default unless a product preference explicitly changes it.

#### Scenario: User selects the combined strategy

- **WHEN** the user selects “on-demand preview plus background full”
- **THEN** the client ensures the current cursor window through the on-demand provider
- **AND** starts one persistent full Run for the same scientific configuration
- **AND** keeps both identifiers and lifecycles separately.

#### Scenario: User selects full only

- **WHEN** the user selects “full only”
- **THEN** the client uses the existing full Run path
- **AND** it does not create an on-demand subscription.

### Requirement: Separate preview and background status

The WPF detail page SHALL expose preview target/display state separately from background full Run status.

#### Scenario: Background progress changes

- **WHEN** the full Run reports progress
- **THEN** the progress text and completed/total window count update in the background status surface
- **AND** the current chart, cursor position, playback state, and selected display projection remain unchanged unless the newly completed window is an exact current-target match.

#### Scenario: Preview is unavailable

- **WHEN** the current on-demand target is rejected, insufficient, cancelled, or failed
- **THEN** the page shows the structured reason for that target
- **AND** it does not replace the reason with the background Run's unrelated progress state.

### Requirement: Protect configuration generations

The WPF client SHALL assign a new configuration generation whenever a scientific parameter, recording, montage/channel mapping, reference, filter, dynamic window, refresh step, or algorithm selection changes.

#### Scenario: Old work returns after configuration change

- **WHEN** a response or progress notification belonging to an older configuration generation arrives
- **THEN** it cannot update the current detail page
- **AND** the page preserves the new configuration's cursor and display state.

### Requirement: Keep complete export gated

The WPF client SHALL distinguish a ready preview from a completed full Run when enabling export or full-range statistics.

#### Scenario: Full Run is still running

- **WHEN** the background full Run is queued or running
- **THEN** full-range export and statistics remain unavailable or explicitly marked partial
- **AND** the user can continue browsing the ready on-demand window.

### Requirement: Release page-owned resources only

Closing or navigating away from the detail page SHALL release page-owned polling and on-demand subscriptions without deleting historical results or silently cancelling the background full Run.

#### Scenario: Page is reopened

- **WHEN** the user reopens the same recording and scientific configuration
- **THEN** the client can reconnect to the existing background Run and/or verified window cache
- **AND** it does not create duplicate active work for the same identity without an explicit retry.

### Requirement: Reflect algorithm capability in strategy availability

The WPF client SHALL derive strategy availability from the selected algorithm capability declaration. An algorithm without validated on-demand support SHALL expose only the existing full strategy, with a structured explanation; it SHALL NOT silently create an on-demand session or silently start a full Run as a substitute for the unavailable combined strategy.

#### Scenario: Combined strategy is unsupported

- **WHEN** the selected algorithm does not support validated single-window execution or cross-mode reuse
- **THEN** “on-demand only” and “on-demand preview plus background full” are disabled or hidden according to the shared UI policy
- **AND** the reason identifies the missing capability or evidence.

### Requirement: Preserve request generations and result provenance in the detail page

The WPF client SHALL track a request generation for each preview target and SHALL display the result scope and source without conflating producer mode with the source that served the current chart.

#### Scenario: Background result is served to the chart

- **WHEN** a verified full-artifact window becomes available for the current target
- **THEN** the chart may display it only if its scientific identity and exact half-open window match the current generation
- **AND** the UI records whether the result was produced by `full` or `on_demand` and whether it was served from computation, an on-demand artifact, or a full artifact index.

#### Scenario: Old generation completes

- **WHEN** a preview response belongs to an older request generation
- **THEN** the response cannot replace the current chart or cursor state
- **AND** it may remain available to the cache or historical task surface.

### Requirement: Expose complete coverage separately from preview readiness

The WPF client SHALL show background full completion using persisted completed-window count, total-window count, coverage state, quality/failure summaries, and artifact validation state. A ready preview SHALL never enable controls that require a complete full-range artifact.

#### Scenario: Full artifact is not complete

- **WHEN** the background Run has unresolved windows or an invalid/incomplete artifact manifest
- **THEN** full-range export and statistics remain disabled or explicitly marked partial
- **AND** the current on-demand chart remains usable.

