# desktop/ant-eego-adapter Specification

## Purpose
TBD - created by archiving change add-ant-eego-c-abi-adapter. Update Purpose after archive.
## Requirements
### Requirement: ANT/eego adapter is optional and does not bundle vendor material

The desktop client SHALL load the ANT/eego SDK only from an explicit local x64
DLL path at runtime. It SHALL not copy vendor DLLs, drivers, headers, or sample
application code into source control or build output.

#### Scenario: SDK path is missing or cannot load

- **WHEN** an ANT/eego adapter is configured with a missing or unloadable SDK path
- **THEN** discovery or stream opening SHALL fail with a structured acquisition error
- **AND** the desktop process SHALL remain usable
- **AND** no device or EEG sample SHALL be fabricated.

### Requirement: Adapter uses device-returned stream identity and configuration

The adapter SHALL discover device IDs/serials, supported sampling rates,
channel index/type list, and range lists from the SDK. It SHALL require
explicit reference and bipolar range configuration and validate each against
the selected device before opening its stream.

#### Scenario: Range is not configured or unavailable

- **WHEN** the configured reference or bipolar range is absent or not reported by the device
- **THEN** stream opening SHALL fail before opening a stream
- **AND** no implicit first/default range SHALL be selected.

### Requirement: Adapter preserves sample-major data and sample-counter semantics

The adapter SHALL retain the opened stream's actual channel order and decode
data as sample-major doubles with an explicit unit per channel. It SHALL use
the stream's actual sample-counter channel to construct batch counters.

#### Scenario: Counter data within a batch is invalid

- **WHEN** a sample-counter value is non-finite, non-integral, negative, or does not increment by one within a received batch
- **THEN** the adapter SHALL terminate that stream with an explicit continuity error
- **AND** it SHALL not interpolate, reorder, or invent samples.

#### Scenario: SDK does not provide an electrode label

- **WHEN** the SDK returns only a native channel index and type
- **THEN** the adapter SHALL leave the electrode label unset
- **AND** it SHALL not infer a 10-20 label from channel order or index.

### Requirement: The vendor-native stream is serialized process-wide

The adapter SHALL permit no more than one active vendor EEG stream in the
desktop process and SHALL close the stream before closing its amplifier/native
runtime ownership.

#### Scenario: A second stream is requested while one is active

- **WHEN** another ANT/eego stream open is requested while a stream is active
- **THEN** the request SHALL fail with an explicit already-active error
- **AND** the active stream SHALL remain unchanged.

