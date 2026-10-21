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
| Theta/Beta | `app/algorithms/theta_beta/compute.py` | same |
| Brainbeat | `app/algorithms/brainbeat/official.py`, `compute.py` | same |
| Definition validation | `app/definitions/validation.py` | same |
| Official catalog | `app/algorithms/catalog.py` | same |
| Generic execution | `app/algorithm_runtime/` | same |

## Compatibility Adapters

No forwarding facade remains for the migrated spectral primitives, official
IAPF/FAA/RBP/Theta-Beta formulas, or the retired metric playback processor.
Tests and diagnostic scripts import the canonical owners directly.

## Validation References

The following remain executable only from validation tests and are not Runtime
registration targets:

- `app/services/independent_spectral_reference.py`

## Historical References And Definition Engine

The legacy metric playback transport and stateful `EEGProcessor` execution
stack and historical graph/formula executors were retired. Current Definition
metadata validation does not execute graphs or formulas:

- `app/definitions/validation.py` (topology and parameter validation)
- `app/eeg_core/primitives/` (closed vocabulary used by current validation)

Historical shadow modules and their CLI are removed. Current independent
references and frozen artifacts remain in `backend/tests` and `docs/validation`.

## Rules

1. New PSD, filtering, quality, or official algorithm code goes only in the
   active authority listed above.
2. New compatibility forwarding modules require a real product caller and a
   documented removal gate; test-only aliases are not retained.
3. Validation references are never registered with Runtime.
4. Raw data, Run records, and stored artifacts remain outside this migration.
