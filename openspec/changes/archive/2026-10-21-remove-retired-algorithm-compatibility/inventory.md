# Retirement inventory and frozen baseline

Starting commit: d1c09a3. No real EEG input is needed for this cleanup.

## Consumers and ownership

| Removed boundary | Consumers / replacement |
| --- | --- |
| Historical shadow package and realtime spectral helpers | Shadow tests, helper tests and `scripts/run_official_algorithm_shadows.py` removed. Current metadata assertions retained in `test_official_definition_metadata.py`. |
| Old Brainbeat module and realtime manifest | Current `compute_brainbeat` and two numerical audit suites still used its ratio. Formula moves unchanged to `algorithms/brainbeat/official.py`; no realtime descriptor remains. |
| `app/legacy/definition_engine.py` | DefinitionService keeps graph/schema validation through `app/definitions/validation.py`; unused graph/formula execution and its tests removed. Primitive vocabulary is retained for current Definition validation. |
| Old FAA aliases/discard/cap constants / realtime contract | No active consumer; constants and realtime-only assertions removed. Current FAA paired-epoch formula and offline contract remain unchanged. |
| Old three-channel Theta/Beta bundled metrics | Only tests used this retired calculator. Deleted calculator and bundled reference; current numerical tests compare single-channel `compute_theta_beta` with independent IAPF/band-power inputs, retaining all frozen artifact bytes and tolerances. |
| Old catalog contract location and manifest alias | Current DTO moves to `algorithms/catalog_contracts.py`; unused manifest alias and old package are removed without a shim. |
| Old cached-window provider calling convention | All current providers accept `check_cancelled`; delete signature probing and the two-argument compatibility branch from RunExecutionContext. |
| Historical reference authority entry | Replaced with the existing independent SciPy/MNE spectral reference service; this is validation-only and is not registered for execution. |
| WPF inferred STFT provenance | Both preview paths validate real evidence. Backend saved-window projection now transports persisted provenance through the DTO. |

The dependency gate scans `backend/app`, `backend/scripts`, and `backend/tools` for retired imports. Historical reports are records, not supported execution entry points. Database schema migrations, device compatibility, montage compatibility and cache corruption protections are outside this deletion scope.

## Immutable numerical evidence

Reference: `analytic-numpy-reference-v1`; NumPy 2.3.4 / SciPy 1.16.3. Comparison tolerances remain in the existing baselines and tests. All nine algorithms retain full/on-demand/combined capability and directional-reuse declarations as recorded by the strategy matrix.

| File under docs/validation | SHA-256 |
| --- | --- |
| official-algorithm-numeric-baseline-arrays.npz | e317119d157ed7cb612529c27c899744af30c0026aa757b603df0a9b2ec2e490 |
| official-algorithm-numeric-baseline.json | 2d4c5f0c748ba2c3d9bf5d8c285c4310319422f0c267d595d1194d39ef060575 |
| official-algorithm-strategy-baseline.json | 0f1ce0e368f9f1b2a646535272f455db836aa236a268bd74ce7739522131651d |
| official-algorithm-strategy-baseline.npz | 32b8ad9ed72dd0f03c73a2d363ca64d74670412fd9fa4d578dc9411475e7d965 |
| official-algorithm-strategy-matrix.json | 1ce348b2dd0d5bac8ddfba9a16d5fec4ace703785094609012656c189b94fee8 |

No expected arrays or tolerances are regenerated as part of cleanup. Any comparison failure must report its coordinate and error before acceptance.
