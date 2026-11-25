# Proposal

## Why
ICA applies a multichannel transform only to complete input rows. Per-channel QC gaps therefore make additional output channels unavailable, but current preview and algorithm evidence cannot distinguish that dependency from newly detected artifacts.

## What Changes
- Record exact missing-input and additional output-unavailability intervals centrally for applied offline artifact removal.
- Keep the authoritative detector report unchanged; expose separate application validity in preview, desktop status and spectral quality evidence.
- Version application/cache evidence without changing ICA fitting or component selection.

## Capabilities
### New Capabilities
None.
### Modified Capabilities
- `signal/preprocessing`: distinguish detected artifacts from transform reconstruction unavailability.

## Impact
Scientific removal results, prepared source identities, preview API and its WPF consumer, spectral quality reports and focused tests. No new dependency, file migration, raw-data modification or changes to units, channel order, timestamps or ICA mathematics. API fields are additive; desktop consumes the synchronized response. Non-goals include automatic input imputation and a new ICA fitting strategy. Existing incomplete changes are preserved; this separately authorized repair does not archive them.
