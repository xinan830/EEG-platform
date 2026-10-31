## Purpose

Provide one typed, sample-coordinate recording access boundary so Review,
algorithm panels, and future live consumers can share recording identity and
window semantics without knowing file chunks or ring-buffer storage details.

## ADDED Requirements

### Requirement: Recording access sessions expose scientific source metadata

Every opened recording access session MUST expose a stable recording identity,
an access revision, sampling rate in Hz, the ordered source channel table, the
source unit for each channel, the first and last available sample counters, and
the explicit recording gaps. The duration MUST be derived from sample counters
and sampling rate rather than a UI clock. Channel order MUST be the order of the
source stream, and the session MUST identify the sample coordinate system it
uses.

#### Scenario: Consumer opens a historical recording

- **WHEN** a consumer opens a valid recording by its expected recording identity
- **THEN** the session returns the source metadata before any window is read,
  including the sampling rate, ordered channels, units, sample bounds, and gaps

#### Scenario: Source metadata is invalid

- **WHEN** the adapter cannot validate the recording identity, sampling rate,
  channel table, units, or sample bounds
- **THEN** opening fails with a stable structured recording-access error and no
  partially initialized session is published

### Requirement: Recording windows use integer sample coordinates

Consumers MUST request half-open sample ranges `[start_sample, end_sample)` and
MUST specify the channel selection explicitly or request the complete source
channel order. A successful response MUST return the requested range, the actual
available range, the sampling rate, the selected channel order and units, and
one or more sample-major segments. The adapter MUST NOT convert a request to a
different time basis or silently synthesize samples.

#### Scenario: Read a bounded source window

- **WHEN** a consumer requests a valid range and channel selection
- **THEN** the session returns only that bounded range with sample counters and
  sample-major float64 values in source units

#### Scenario: Request crosses a recording boundary

- **WHEN** a requested range extends outside the available sample bounds
- **THEN** the response reports the requested and actual ranges explicitly and
  returns only available samples; a request with no available overlap returns a
  structured no-data error

### Requirement: Recording gaps remain explicit

The access boundary MUST preserve every known gap or discontinuity as metadata
and MUST split returned data at a gap. It MUST never fill a gap with zeros,
repeated samples, interpolation, or a prior frame. Consumers MUST be able to
distinguish an empty segment caused by a gap from an unavailable recording.

#### Scenario: Read across a persisted gap

- **WHEN** a requested range contains a known missing sample interval
- **THEN** the response contains separate contiguous segments and identifies the
  missing interval without fabricating values

### Requirement: Session lifetime and reads are bounded and cancellable

The recording session manager MUST provide at most one live access session for
the same recording identity and source revision within a workspace. It MUST
reuse the session's validated index and reader lifetime, bound in-flight source
work according to the adapter's declared capacity, and honor cancellation for
queued and active reads. Closing a session MUST prevent new reads and release
its source resources after admitted reads exit.

#### Scenario: Two consumers open the same recording

- **WHEN** Review and an algorithm adapter request the same recording identity
  and revision
- **THEN** the manager returns the same session-owned source lifetime rather
  than opening a second independent reader

#### Scenario: Consumer cancels a slow read

- **WHEN** a consumer cancels a queued or active window read
- **THEN** the read terminates with cancellation, does not publish a partial
  frame, and does not leave a session slot permanently occupied

### Requirement: Adapter failures are explicit

The access boundary MUST return stable error codes for invalid identity,
missing/incomplete source, invalid ranges, disposed sessions, cancellation and
I/O failures. A failed read MUST NOT be represented as an empty numeric array,
zero-valued samples, or a previous successful frame. The boundary MUST include
recording identity and requested sample range in diagnostic context without
logging raw EEG payloads.

#### Scenario: Source chunk is unreadable

- **WHEN** a required raw chunk cannot be opened or decoded
- **THEN** the consumer receives a structured read failure with the recording
  identity, sample range and stable error code, and no numeric result is bound

### Requirement: Consumers are independent of storage adapters

Review and algorithm consumers MUST depend only on the recording access contract.
The local immutable-recording adapter MUST read existing WPF raw recordings;
future live ring-buffer access MUST be implemented as another adapter with the
same contract. No consumer may inspect chunk paths, memory maps, queue internals
or ring-buffer eviction details.

#### Scenario: Review and algorithm panel share one recording

- **WHEN** both consumers request windows from one workspace recording
- **THEN** both use the same access contract and sample coordinate semantics
  while retaining independent loading and presentation lifecycles
