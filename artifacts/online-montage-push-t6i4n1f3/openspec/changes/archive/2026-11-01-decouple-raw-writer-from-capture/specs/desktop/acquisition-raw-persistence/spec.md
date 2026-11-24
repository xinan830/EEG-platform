## MODIFIED Requirements

### Requirement: Device capture must not await each raw-file write
The WPF acquisition runtime SHALL enqueue accepted raw batches onto a bounded,
ordered persistence pipeline instead of waiting for each disk write before
reading the next vendor batch. The pipeline SHALL preserve sample order and
drain accepted operations before completing a recording.

#### Scenario: Slow storage during a high-rate stream
- **WHEN** raw persistence takes longer than one device batch interval
- **THEN** the capture loop continues reading and processing vendor batches
- **AND** accepted raw batches are written in sample-counter order
- **AND** recording completion waits for the persistence queue to drain

#### Scenario: Persistence queue reaches capacity
- **WHEN** the bounded raw-write queue cannot accept another batch
- **THEN** the recording enters a structured raw-persistence fault
- **AND** the batch is not silently discarded or represented as valid data

#### Scenario: Raw writer fails after batches were accepted
- **WHEN** the underlying writer reports an I/O failure
- **THEN** the failure is retained and surfaced as an acquisition fault
- **AND** completion or abort does not report a successful complete recording
