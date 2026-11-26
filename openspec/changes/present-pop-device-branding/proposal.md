# Proposal

## Why
Users should see POP consistently. Brand text currently mixes presentation with system-preset identity, so simple replacement can break montage deduplication.

## What Changes
- Centralize POP product display names for devices, default channel layouts, system montages and application messages.
- Identify system presets by declared source and stable identity/structure rather than an ANT name prefix.
- Present historical built-in profiles through identity-based display properties without rewriting snapshots, fingerprints or user-authored names.

## Capabilities
### New Capabilities
None.
### Modified Capabilities
- `desktop/device-session`: POP display names independent of native device/driver identities.
- `montage-presentation`: identity-based default profile presentation and deduplication.

## Impact
Desktop device/channel/montage/acquisition/review presentation, tests and manual. No API, database, SDK ABI, algorithm, signal unit/time/order, quality or provenance change. Existing native IDs, vendor evidence and serialized historical names remain intact; no ANT/POP name aliases, migration or global replacement. Rollback restores display code only.
