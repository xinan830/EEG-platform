## ADDED Requirements

### Requirement: RBP SHALL preserve selected montage output order
The RBP execution request SHALL resolve its channel list from the selected
recording montage snapshot, reject unavailable or stale outputs, and preserve
the snapshot order in the result's `channel_order`.

#### Scenario: Resolve an available historical montage
- **WHEN** the selected snapshot contains available outputs `F3-REF` then `F4-REF`
- **THEN** the RBP request resolves exactly that ordered list
- **AND** the result reports `channel_order` as `F3-REF`, `F4-REF`

#### Scenario: Reject a stale or unavailable output
- **WHEN** the requested montage revision or fingerprint differs from the recording snapshot
- **THEN** the request is rejected with a structured montage identity error

### Requirement: RBP SHALL return one five-band result per output channel
For every planned analysis window, RBP SHALL return Delta, Theta, Alpha, Beta,
and Gamma relative-power values for each resolved output channel. Values SHALL
use ratio units and each clean channel's five values SHALL sum to one within the
declared numerical tolerance.

#### Scenario: Return two channel results for one window
- **WHEN** a `0–5 s` window resolves `F3-REF` and `F4-REF`
- **THEN** the result contains five ratio values for each channel
- **AND** each clean channel's five values sum to one within tolerance

### Requirement: RBP SHALL keep channel failures explicit
An unavailable or quality-rejected channel SHALL carry null band values and a
structured channel-specific failure. The implementation SHALL NOT replace the
values with zero or invalidate other clean channels in the same window.

#### Scenario: Keep a clean channel when another channel fails
- **WHEN** `F3-REF` fails the quality gate and `F4-REF` passes
- **THEN** F3 carries null band values and a failure reason
- **AND** F4 retains its computed five-band values

### Requirement: Desktop SHALL append channel history by window identity
The desktop RBP history projection SHALL append accepted channel/window points
by recording, montage identity, configuration identity, channel, and half-open
sample bounds. Duplicate or stale points SHALL NOT overwrite a newer context.

#### Scenario: Append consecutive sliding windows
- **WHEN** completed windows `0–5 s` and `1–6 s` arrive for the same channel and context
- **THEN** the chart history contains both points ordered by window end time

#### Scenario: Ignore a duplicate or stale window
- **WHEN** the same window arrives twice or arrives from an older montage context
- **THEN** the second or stale point is not rendered in the active history

### Requirement: Desktop SHALL render channel-specific stacked history
The desktop SHALL render one five-band 100% stacked history chart per selected
montage output, using absolute window end time on the x-axis and preserving
gaps for unavailable points.

#### Scenario: Render all selected montage outputs
- **WHEN** the selected montage has `F3-REF` and `F4-REF`
- **THEN** the RBP detail view exposes one stacked history chart for each output
- **AND** each chart uses the five RBP bands and a ratio axis from zero to one

#### Scenario: Preserve an unavailable gap
- **WHEN** one channel is unavailable for the `1–6 s` window
- **THEN** that channel's stacked history has a gap at six seconds rather than a zero-valued band

