# Scientific Backend Ownership Inventory

This inventory is the source of truth during the incremental backend
restructure. A file may remain in `app/eeg_core` while its ownership is being
migrated, but new active scientific code MUST NOT be added there.

## Active Authorities

| Area | Current authority | Target authority |
| --- | --- | --- |
| Offline analysis contract | `app/scientific/contracts/analysis.py` | same |
| Spectral quality gate | `app/scientific/quality/spectral.py` | same |
| Preprocessing, Welch PSD, band power, spectrogram | `app/scientific/primitives/spectral.py` | same |
| IAPF | `app/algorithms/iapf/official.py` | same |
| FAA | `app/algorithms/faa/official.py` | same |
| RBP band contract and Runtime execution | `app/algorithms/rbp/official.py`, `runner.py` | same |
| Theta/Beta | `app/algorithms/theta_beta/official.py` | same |
| Official catalog | `app/algorithms/catalog.py` | same |
| Generic execution | `app/algorithm_runtime/` | same |

## Compatibility Adapters

These paths are retained only so existing imports and historical callers keep
working. They MUST delegate and MUST NOT add formulas or policy:

- `app/eeg_core/spectral.py`
- `app/eeg_core/official_algorithms/faa.py`
- `app/eeg_core/official_algorithms/iapf.py`
- `app/eeg_core/official_algorithms/rbp.py`
- `app/eeg_core/official_algorithms/theta_beta.py`

## Validation References

The following remain executable only from validation tests and are not Runtime
registration targets:

- `app/eeg_core/official_algorithms/validation/`
- `app/services/independent_spectral_reference.py`

## Legacy Realtime/Definition Engine

These paths still support the existing realtime or historical user-definition
chain. They are frozen for new active science and will move behind `legacy`
only after historical-read and desktop compatibility tests pass:

- `app/eeg_core/processor*.py`
- `app/eeg_core/stream_filter.py`
- `app/eeg_core/realtime_spectral.py`
- `app/eeg_core/iapf_*.py`
- `app/eeg_core/definition_engine.py`
- `app/eeg_core/primitives/` (historical definition graph vocabulary)
- `app/algorithms/user_definition/`

## Rules

1. New PSD, filtering, quality, or official algorithm code goes only in the
   active authority listed above.
2. A compatibility adapter may import its target, but the target may not import
   the adapter.
3. Validation references are never registered with Runtime.
4. Raw data, Run records, and stored artifacts remain outside this migration.
