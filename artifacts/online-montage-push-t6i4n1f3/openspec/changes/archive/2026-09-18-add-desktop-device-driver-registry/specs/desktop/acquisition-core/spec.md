## ADDED Requirements

### Requirement: Acquisition device drivers are explicitly registered

The desktop SHALL create device adapters only through an explicit registry of
installed drivers. The configured acquisition runtime SHALL depend on the
common driver and adapter contracts and SHALL NOT instantiate or reference a
vendor-specific adapter. A driver configuration SHALL contain a stable driver
identity plus opaque vendor settings that only the selected driver interprets.

#### Scenario: A second vendor driver is installed

- **WHEN** a registered non-ANT driver receives a valid configuration
- **THEN** the configured runtime SHALL create and use that driver's common
  acquisition adapter for discovery and streaming
- **AND** the coordinator, raw writer, display pipeline, and Python analysis
  boundary SHALL remain vendor-neutral

#### Scenario: A selected driver is not installed

- **WHEN** configuration names a driver absent from the registry
- **THEN** configuration SHALL fail with an explicit unavailable-driver error
- **AND** no vendor SDK, device, channel table, sampling rate, or EEG batch
  SHALL be fabricated

### Requirement: Device adapter ownership is released by the common lifecycle

Every common acquisition adapter SHALL support asynchronous disposal. The
configured runtime SHALL dispose its coordinator and active stream before
disposing the adapter.

#### Scenario: Reconfiguration or application shutdown occurs

- **WHEN** the configured runtime replaces or disposes an adapter
- **THEN** it SHALL stop and dispose the coordinator before disposing the
  vendor adapter
- **AND** a vendor implementation SHALL remain responsible for its SDK-native
  stream and handle teardown
