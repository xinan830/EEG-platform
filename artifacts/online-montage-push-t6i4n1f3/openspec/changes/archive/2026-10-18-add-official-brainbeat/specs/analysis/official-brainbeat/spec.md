## ADDED Requirements

### Requirement: Brainbeat is a runnable official AnalysisRun algorithm
The backend SHALL expose Brainbeat as a runnable official algorithm for static
and dynamic AnalysisRun execution. It SHALL calculate relative Theta power from
the explicit Fz source divided by relative Alpha power from the explicit Pz
source, using the IAPF estimated independently for the current window.

#### Scenario: Static Brainbeat execution
- **WHEN** a valid static Run supplies distinct Fz and Pz channels and a usable PSD/IAPF window
- **THEN** the backend returns one finite ratio with unit `ratio`
- **AND** the result records the explicit source channels and calculation trace

#### Scenario: Dynamic Brainbeat execution
- **WHEN** a valid dynamic Run supplies a supported window and step
- **THEN** the backend evaluates each planned window independently
- **AND** no value is carried across windows through EMA or other state

### Requirement: Brainbeat source channels are explicit and distinct
The Run configuration SHALL require both a primary Fz channel and a distinct
secondary Pz channel. The backend SHALL resolve channel names against the
recording without inferring roles from channel order or substituting a missing
channel.

#### Scenario: Missing or identical channel pair
- **WHEN** Pz is missing or the two configured channels are identical
- **THEN** configuration or input resolution fails with a structured validation error
- **AND** no scientific result is produced

### Requirement: Brainbeat preserves unavailable semantics and quality evidence
If the PSD quality gate or IAPF cannot produce a valid value, Brainbeat SHALL
return `null` for that result and preserve the structured failure code, quality
evidence, requested range, and actual range. It SHALL NOT replace unavailable
values with zero or a previous-window value.

#### Scenario: Rejected dynamic window
- **WHEN** one dynamic window fails the PSD quality gate or IAPF requirement
- **THEN** that window is marked rejected or unavailable with a non-empty reason
- **AND** later windows remain independently executable
