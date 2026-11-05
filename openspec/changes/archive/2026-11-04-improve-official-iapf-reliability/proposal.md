## Why

Official IAPF can currently return a numeric COG when any positive Alpha residual exists, even without convincing periodic Alpha activity. Its five-second default also provides only one complete four-second Welch segment, limiting spectral averaging.

## What Changes

- Require independently supported Alpha estimates; return null with a structured reason when neither PAF nor COG passes its criterion.
- Set dynamic IAPF to a 20-second window and one-second step, and suppress warm-up values until a complete window exists.
- Version the changed IAPF scientific contract and preserve spectral/quality evidence.
- **Compatibility impact:** new Runs use IAPF scientific version v5; old persisted results remain historical and unchanged. No API shape or database migration is planned.
- **Non-goals:** multi-channel aggregation, eyes-open/closed metadata, replacing the 1/f model with specparam, or clinical validation.

## Capabilities

### New Capabilities

### Modified Capabilities
- `analysis/official-algorithm-execution`: define IAPF peak/COG acceptance and complete-window dynamic behavior.

## Impact

Backend IAPF estimator, manifest and dynamic policy, shared scientific analysis contract, IAPF tests, and the official algorithm execution spec. WPF reads the updated catalog policy; no client-side calculation changes are required.
