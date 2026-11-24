## ADDED Requirements

### Requirement: Superseded execution hands off to the latest mode

The desktop SHALL centrally invalidate obsolete analysis execution whenever the selected algorithm, execution mode, recording, montage/channel mapping, or scientific configuration changes. It SHALL request cancellation for an obsolete full Run, stop waiting for its result locally once cancellation is accepted, and start the latest selected strategy without waiting for stale result polling to finish.

#### Scenario: Switch from full analysis to on-demand analysis

- **WHEN** a full Run is queued or running and the user selects on-demand analysis
- **THEN** the desktop requests cancellation of the old Run and starts the current on-demand window request as soon as the Run identity is known
- **AND** stale full Run progress or results do not replace the current on-demand state
- **AND** a backend window already inside scientific computation may finish its safe cancellation boundary before the interactive window acquires the shared compute slot.

#### Scenario: Switch algorithms during full analysis

- **WHEN** a full Run is active and the user selects another algorithm or changes its scientific configuration
- **THEN** the old Run is marked obsolete by the shared execution coordinator
- **AND** the newest configuration starts without waiting for the old client's polling loop to observe a terminal status.

#### Scenario: Run creation response arrives after the configuration changes

- **WHEN** a Run was submitted but its identifier had not yet been returned when the configuration changed
- **THEN** the client cancels it immediately after receiving its identifier
- **AND** it does not publish the stale Run as current or apply its result to the new configuration.

#### Scenario: Backend cancellation reaches a running scientific window

- **WHEN** cancellation is requested while a backend scientific window is executing
- **THEN** the backend records the cancellation request and exits at its existing safe cancellation checkpoint
- **AND** no claim is made that native NumPy/SciPy work is forcibly interrupted mid-window.
