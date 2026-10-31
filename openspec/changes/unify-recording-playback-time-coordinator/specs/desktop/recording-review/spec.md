## MODIFIED Requirements

### Requirement: Playback clock is independent from waveform window loading

The desktop SHALL advance the visible playback position from the single recording-session time coordinator using a monotonic clock without rereading and reprojecting the complete visible window on every UI tick. It SHALL pan a continuous visible time range inside a bounded nearby-data cache and SHALL atomically replace cache blocks only after the target block is complete.

#### Scenario: A 4000 Hz recording plays within one visible page

- **WHEN** the playback clock advances repeatedly before the current page end
- **THEN** the session coordinator publishes the recording-relative cursor and the displayed position and cursor advance smoothly
- **AND** those clock events do not trigger additional raw-window reads while the current bounded cache covers the target
- **AND** the next bounded page MAY be prefetched before the boundary.

#### Scenario: An algorithm detail panel is hosted in the same recording workspace

- **WHEN** Review and an algorithm detail panel are hosted in the same recording workspace
- **THEN** both consume the same coordinator cursor and generation
- **AND** the algorithm page does not create an independent playback clock or alter the waveform clock.
