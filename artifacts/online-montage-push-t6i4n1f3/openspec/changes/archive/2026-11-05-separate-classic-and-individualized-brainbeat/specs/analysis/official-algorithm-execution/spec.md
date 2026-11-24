## ADDED Requirements

### Requirement: Execute classic and individualized BrainBeat with distinct scientific contracts
Official Run SHALL execute classic `brainbeat` and `brainbeat_individualized` through distinct current runtime modules and configuration identities. Classic BrainBeat SHALL return the absolute Fz 4–8 Hz power divided by absolute Pz 8–12 Hz power and SHALL not require IAPF. The individualized variant SHALL retain the IAPF-relative relative-power calculation and return null when its IAPF estimate or effective bands are unavailable. Both SHALL use explicit distinct Fz/Pz source mappings, the shared preprocessing and Welch quality contract, and the selected window's actual range. Historical stored Run payloads SHALL remain unchanged and SHALL NOT be recomputed or reinterpreted.

#### Scenario: Create a classic BrainBeat Run
- **WHEN** a valid recording and explicit distinct Fz/Pz channels are submitted for classic BrainBeat
- **THEN** the completed Run reports the fixed bands, absolute intermediate powers, dimensionless ratio, quality state, actual range, and current scientific identity
- **AND** a missing IAPF does not reject the classic result

#### Scenario: Create an individualized BrainBeat Run
- **WHEN** a valid recording and explicit distinct Fz/Pz channels are submitted for the individualized variant
- **THEN** the Run uses its IAPF-relative bands and relative-power denominators
- **AND** unavailable IAPF yields null with a stable structured reason

#### Scenario: Reject incomplete or aliased role mappings
- **WHEN** a Run omits Fz or Pz, maps both roles to one source, or names an unavailable source channel
- **THEN** the request fails with a structured validation error
- **AND** the runtime does not infer or substitute either channel

#### Scenario: Preserve previously stored Run evidence
- **WHEN** a historical BrainBeat Run created before this change is read
- **THEN** the API returns its stored scientific identity and value without rewriting it
- **AND** new requests use only current classic or individualized identities

### Requirement: Use the declared BrainBeat dynamic policy for Run and on-demand windows
Official Run and on-demand execution for each BrainBeat identity SHALL default to the catalog-declared 20-second trailing window and one-second step. Supported window values SHALL be 10, 20, and 30 seconds. No numeric warm-up result SHALL be emitted before a complete selected window exists. Every dynamic result SHALL carry the backend-computed absolute start/end range and endpoint time.

#### Scenario: Align Run and review calculations
- **WHEN** an Official Run and an on-demand review calculation use the same BrainBeat identity, source channels, preprocessing, and actual range
- **THEN** their numerical values, quality, and calculation evidence match within the declared validation tolerance

#### Scenario: Reject an unsupported window selection
- **WHEN** a request supplies a BrainBeat dynamic window outside the declared supported values
- **THEN** the request fails with a structured configuration error
- **AND** the runtime does not silently clamp or substitute a different window
