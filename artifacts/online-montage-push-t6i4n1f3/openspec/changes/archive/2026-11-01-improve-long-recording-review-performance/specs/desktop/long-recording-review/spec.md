# Long-Recording Review Performance

## ADDED Requirements

### Requirement: Review performance stages are observable without raw-data leakage

The review pipeline SHALL expose bounded diagnostic evidence for navigation,
raw read, filter/checkpoint work, result lookup or computation, frame publication,
and chart binding. Evidence SHALL identify the recording, session, target,
source path, point count, and elapsed time without logging raw EEG values.

#### Scenario: A user seeks to a cold location

- **WHEN** a review target is outside the current complete frame
- **THEN** diagnostics SHALL distinguish raw read, filter/checkpoint processing,
  result lookup/computation, and frame publication timings
- **AND** the record SHALL include the requested absolute sample/time and source
  path
- **AND** it SHALL not include the complete waveform payload.

### Requirement: Review keeps target and loaded frame identities separate

The review SHALL maintain the newest requested target separately from the last
complete loaded/published frame. An obsolete response SHALL NOT replace a newer
target or relabel older samples with newer time coordinates.

#### Scenario: A user changes target during an outstanding load

- **WHEN** target B supersedes target A before A completes
- **THEN** A SHALL be cancelled or ignored when possible
- **AND** A SHALL NOT publish a frame for B's viewport
- **AND** a complete current-identity frame SHALL be required before replacing
  the published frame.

#### Scenario: A drag target waits in the interaction throttle

- **WHEN** the user accepts target B while target A is loading, even before
  B's throttled load begins
- **THEN** A SHALL already be fenced from frame publication
- **AND** the last complete frame SHALL retain its original sample coordinates
- **AND** this user-navigation fencing SHALL NOT restart a reusable block on
  every continuous-playback clock tick.

#### Scenario: An obsolete load fails or finishes after review is closed

- **WHEN** a cancelled or superseded load later returns success or failure
- **THEN** it SHALL NOT replace the latest frame, status, position or loading
  state, and SHALL NOT raise late user-visible errors
- **AND** a disposed review session SHALL NOT publish change notifications
- **AND** a failure of the current active target SHALL still be reported.

#### Scenario: Shared checkpoint work outlives a foreground subscriber

- **WHEN** a foreground request is replaced, returns to a cached frame, or is
  disposed while shared checkpoint work is still active
- **THEN** progress SHALL reach only current subscribers to that source work
- **AND** prefetch-only progress SHALL NOT change foreground status
- **AND** a new current subscriber to the same work MAY reuse its latest progress
  without restarting the scientific work.

#### Scenario: A checkpoint coordinator closes with active and queued work

- **WHEN** the coordinator is closed while an operation is active or queued
- **THEN** both operations SHALL receive cancellation and no late progress SHALL
  be published
- **AND** synchronization resources SHALL remain valid until admitted operations
  have finished releasing them
- **AND** non-cooperative late completion SHALL NOT prevent prompt close.

### Requirement: Visible waveform data remains bounded

Review SHALL bound chart points, frame memory, in-flight requests, and derived
cache usage by explicit budgets independent of total recording duration.

#### Scenario: A multi-hour recording is played continuously

- **WHEN** playback crosses many viewport boundaries
- **THEN** chart series and frame memory SHALL remain within their configured
  bounds
- **AND** completed old frames SHALL be evictable without changing raw data or
  historical results
- **AND** playback ticks inside a cached viewport SHALL not append an unbounded
  history series.

#### Scenario: A single cache entry exceeds the retention budget

- **WHEN** a complete raw window or frame exceeds its numeric-payload cache budget
- **THEN** it SHALL remain intact for the requesting consumer without being retained
- **AND** reusable smaller cache entries SHALL NOT be evicted just to admit it
- **AND** this retention policy SHALL NOT truncate samples or change gaps or units.

#### Scenario: Filtered source admission is saturated

- **WHEN** two filtered source builds are unfinished for one loader
- **THEN** a different build SHALL await cancellable admission without starting raw reads
- **AND** an identical reusable build SHALL still be shared
- **AND** cancelled but unfinished builds SHALL retain admission until they actually exit
- **AND** closing review SHALL cancel admission waiters without awaiting non-cooperative work.

### Requirement: Long recordings support resolution-appropriate waveform display

The review SHALL select a display level from visible absolute time range,
sampling rate, plot width, channel count, and source identity. Each level SHALL
preserve ordered temporal extrema and explicit invalid/gap states.

#### Scenario: A user views a day-scale overview

- **WHEN** the visible range is too wide for sample-level rendering
- **THEN** review SHALL read a compatible overview level instead of all raw
  samples in the recording
- **AND** each bucket SHALL preserve its valid extrema in temporal order
- **AND** recorded gaps SHALL remain visible breaks.
- **AND** a disposable chunk identity SHALL include the selected display level,
  chunk index, and channel index so separate traces cannot collide.

#### Scenario: The selected display level is not complete

- **WHEN** the level index is absent, corrupt, stale, or does not cover the
  complete visible sample range
- **THEN** review SHALL treat it as a cache miss
- **AND** SHALL fall back to the bounded raw/filtered review-window path
- **AND** SHALL NOT publish a partial display level as a complete waveform.

#### Scenario: A user zooms into a short interval

- **WHEN** the visible range becomes small enough for a finer level
- **THEN** review SHALL select a finer compatible level or bounded raw samples
- **AND** the displayed values SHALL retain the declared unit and channel order.

#### Scenario: A bucket contains a gap or invalid sample

- **WHEN** a display bucket is only partially covered by source samples or
  contains a non-finite value
- **THEN** the bucket SHALL be published as an explicit gap
- **AND** extrema from the two sides of the gap SHALL NOT be combined into a
  continuous display value.

### Requirement: Filtered display levels preserve filter identity

A filtered waveform level SHALL be derived from the compatible filtered source
identity, including filter settings and filter-contract version. Raw waveform
levels SHALL NOT satisfy a filtered display request.

#### Scenario: A user reviews a filtered waveform

- **WHEN** the selected high-pass, low-pass, notch, or filter contract differs
  from a cached level
- **THEN** the level SHALL be treated as a cache miss
- **AND** review SHALL not label raw or differently filtered values as the
  requested filtered waveform.

### Requirement: Completed full-analysis windows are read without duplicate work

When a complete full-analysis result matches the recording, montage, filter,
algorithm version, parameters, and exact scientific window identity, review
SHALL read that result without invoking algorithm computation or filter warmup.

#### Scenario: A full Run is complete before review navigation

- **WHEN** the user seeks to a window covered by the complete compatible Run
- **THEN** the result SHALL be served by indexed read or cache lookup
- **AND** no duplicate algorithm or filter execution SHALL be scheduled
- **AND** the response SHALL retain its original provenance.

#### Scenario: The full result is absent or incompatible

- **WHEN** no compatible complete window exists
- **THEN** review MAY use the existing bounded latest-only on-demand path
- **AND** the unavailable reason/source path SHALL remain observable.

#### Scenario: A cached preview does not belong to the completed Run

- **WHEN** a structured preview has a different Run, artifact Run, scientific
  version, invalid window ordering or inconsistent per-window array shape
- **THEN** it SHALL NOT be bound as the current result
- **AND** review SHALL try verified indexed read before explicit on-demand
  fallback, without relabeling the invalid values as current data.

#### Scenario: Indexed result files are incomplete

- **WHEN** a required index, window chunk or axis file is missing, malformed,
  stale, incompatible or fails its checksum
- **THEN** the backend SHALL return a structured unavailable/integrity error
- **AND** the client MAY fall back to on-demand without modifying the Run.

#### Scenario: A saved warm-up window is scientifically usable

- **WHEN** the backend marks a `Partial` window as usable
- **THEN** reuse SHALL preserve its warm-up state and provenance
- **AND** a scientific rejection SHALL remain unavailable rather than being
  replaced by an older successful window or a fabricated zero.

### Requirement: Long-recording derived indexes are disposable and atomic

Display indexes and caches SHALL be stored outside immutable recordings with a
versioned compatibility identity, bounded storage policy, and atomic publication.
Incomplete, corrupt, stale, or incompatible entries SHALL be treated as misses.

#### Scenario: Index generation is interrupted

- **WHEN** index generation is cancelled or fails before completion
- **THEN** the partial entry SHALL not be readable as a complete level
- **AND** a later request SHALL rebuild or use the existing bounded fallback
  without modifying the raw recording.

### Requirement: Waveform playback time has one owner

The review workspace SHALL use one playback time coordinator as the authority
for play, pause, seek, cursor sample, and playback generation. A waveform
request coordinator SHALL only manage view generations, request identity,
latest-target selection, and cancellation; it SHALL NOT maintain a second
playback clock.

#### Scenario: Playback and waveform loading overlap
- **WHEN** a waveform frame is loading while playback advances
- **THEN** the playback cursor and generation SHALL come from the shared time
  coordinator
- **AND** the waveform request coordinator SHALL request the current target
  without creating or advancing another clock
- **AND** a late frame SHALL be rejected when its generation is obsolete.

#### Scenario: A user seeks during an outstanding request
- **WHEN** a seek changes the playback generation before the old request ends
- **THEN** the old request SHALL be cancelled or fenced
- **AND** it SHALL NOT publish under the new cursor
- **AND** the new target SHALL be scheduled through the same waveform pipeline.

### Requirement: Waveform session and display pipeline have separate ownership

The review SHALL obtain samples through the recording-access session and SHALL
keep recording identity, source revision, channel order, units, display filter
configuration, and session state explicit. Continuous playback and one-off
preview SHALL use independent mutable filter/checkpoint state, even when their
immutable recording and filter configuration is equal.

#### Scenario: Preview and playback overlap
- **WHEN** a one-off preview and continuous playback request overlapping ranges
- **THEN** each operation SHALL use its own filter/checkpoint state
- **AND** one operation SHALL NOT advance, reset, or invalidate the other's
  state
- **AND** both results SHALL retain the same declared source identity and units.

#### Scenario: A recording source changes
- **WHEN** the selected recording or source revision changes
- **THEN** existing waveform state and derived cache entries SHALL be fenced
- **AND** the next frame SHALL be read through the new recording-access session
- **AND** data from the previous source SHALL NOT be relabeled as current.

### Requirement: Only complete identity-matching frames reach the chart

SciChart SHALL receive only an immutable complete frame whose recording,
source revision, view generation, playback generation, request identity,
absolute sample range, channel order, units, and explicit gap state match the
active target. Missing or invalid samples SHALL remain explicit gaps or
structured unavailable states; they SHALL NOT be replaced with zeros or old
values.

#### Scenario: A newer target supersedes an older load
- **WHEN** an older load completes after a newer target was accepted
- **THEN** the older frame SHALL be discarded or retained only as disposable
  cache data
- **AND** it SHALL NOT replace the currently published chart frame
- **AND** the old frame's time and channel metadata SHALL remain unchanged.

#### Scenario: A frame contains a source gap
- **WHEN** the requested range contains a recorded gap or non-finite sample
- **THEN** the frame SHALL carry an explicit gap or unavailable reason
- **AND** the renderer SHALL show a break
- **AND** the pipeline SHALL NOT bridge the gap with fabricated numeric values.

### Requirement: Display processing cannot block the UI or algorithm line

File access, filtering, projection, downsampling, cache I/O, HTTP requests,
and algorithm waits SHALL run outside the UI dispatcher. Waveform rendering
and algorithm result delivery SHALL have independent scheduling and lifetime;
waiting for an algorithm result SHALL not prevent a complete waveform frame
from being published.

#### Scenario: An algorithm request is delayed
- **WHEN** an algorithm computation is queued or slow while a waveform target is
  ready
- **THEN** the waveform frame SHALL publish as soon as its own data is complete
- **AND** algorithm lag SHALL be represented in the algorithm state
- **AND** the UI dispatcher SHALL not synchronously wait for the algorithm.

### Requirement: TNK chart refresh scheduling is coalesced per dispatcher

The TNK detail view SHALL retain one independent SciChart surface per chart
panel while using one shared refresh scheduler per WPF Dispatcher. Multiple
panel refresh requests received before the next background dispatcher turn
SHALL enqueue one dispatcher operation and invoke each current panel callback at
most once. The scheduler SHALL not change chart data, algorithm results,
waveform scheduling, or playback time ownership.

#### Scenario: Several TNK panels receive one result batch

- **WHEN** multiple independent TNK panels publish property changes in the
  same dispatcher interval
- **THEN** the detail view SHALL schedule one background dispatcher operation
- **AND** each affected chart SHALL refresh independently within that operation
- **AND** a repeated request for the same panel before execution SHALL not add
  another callback.
