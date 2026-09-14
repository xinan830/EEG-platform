# Change 04 Official Algorithm Migration Report

## Conclusion

`migrate-official-algorithms` completed engineering shadow validation on
2026-09-14. Existing public result paths remain unchanged. The evidence proves
the declared candidate and legacy calculations agree under engineering
tolerances; it does not establish clinical validity or authorize a realtime
pipeline cutover.

## Evidence

### Synthetic checks

`backend/tests/test_official_algorithm_shadows.py` verifies:

- RBP with requested channel order preserved;
- FAA against an independently implemented paired-epoch Hann reference;
- BrainBeat single-frame ratio and its separate log-domain EMA warmup;
- IAPF Peak and low-quality/unavailable semantics;
- Theta/Beta independently from BrainBeat;
- idempotent persistence of five immutable `platform-official` v1 definitions.

The test suite deliberately tests a quality-failed IAPF value as unavailable,
not zero. The OpenSpec scenario additionally guards against silently mapping a
raw channel such as `O2` to logical `Oz`.

### Local BDF engineering evidence

The local runner was executed on a read-only BDF recording for absolute range
`0.000-30.000 s`, sampled at `500 Hz`. The persisted records below reference
only the local recording identifier/hash and are not committed to Git.

| Metric | ValidationRun | Points | Max abs error | Result |
| --- | --- | ---: | ---: | --- |
| RBP | `7678ad7972f2444f96b9ee65c62ef623` | 16 | 0 | PASS |
| FAA | `a63341b8881345b18818fe2912b19f25` | 1 | 0 | PASS |
| IAPF | `b374b5aea7c94ed79d2866dd0d4be05e` | 1 | 0 Hz | PASS |
| BrainBeat formula + EMA | `b8b33ce66a014908a2b1174e513c7156` | 2 | 0 | PASS |

The selected local recording had `F3`, `F4`, `Fz`, `Pz`, and `O2`, but no
explicit logical `Oz` mapping. Accordingly, the runner rejected local
Theta/Beta persistence with `logical_oz_mapping_required`. Its synthetic
shadow suite passes, but no local mapped-`Oz` claim is made in this report.

BrainBeat's local evidence validates its per-frame formula and EMA state with
offline-preprocessed input only. It expressly does **not** assert parity of
the legacy causal real-time filter/streaming path. That requires a separately
scoped realtime cutover validation.

## Parameters

| Item | Value |
| --- | --- |
| RBP/IAPF PSD | `offline-spectral-v3`: 1-30 Hz, 4 s Hann, clean-segment averaging |
| FAA | full filtered F3/F4 after 12 s discard; 2 s, 50% overlap, paired gate |
| BrainBeat | 2 s Hann PSD; 50% configured overlap; log EMA `alpha=0.15`, warmup `3` |
| Tolerance | `rtol=1e-7`, `atol=1e-9` |
| Internal unit | `float64` V; PSD `V^2/Hz` |
| Source data | read-only local BDF; source samples and filename excluded from Git |

## Remaining boundary

No official endpoint was changed by this work. In particular, FAA, BrainBeat,
and IAPF cannot yet be executed by the generic primitive DAG without an
explicit composite-node implementation. The immutable definitions record that
constraint rather than hiding it. A later cutover must validate the exact
replacement pipeline and retain the old implementation identity for historical
results.
