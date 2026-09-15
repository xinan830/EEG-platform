# Official Algorithm Migration

## Scope

Change 04 introduces immutable official-definition records and backend-only
shadow comparisons.  It does not switch any public spectrum, spectrogram,
offline-analysis, Viewer, or realtime output to the definition executor.
`PASS` means that the compared numerical contract agrees under its stated
engineering tolerance.  It is not evidence of clinical validity.

## Official definitions

On demand, `ensure_official_definitions()` stores and publishes `1.0.0`
records owned by `platform-official`.  The RBP definition is a normal graph of
closed primitives. FAA and BrainBeat use an explicitly declared
`official_composite_shadow_only` boundary because the generic graph engine
cannot faithfully express all of their semantics yet. IAPF and Theta/Beta use
the separate `official_composite_run_adapter` path: they remain composite
algorithms, but run through their owned backend modules rather than a
simplified generic graph.

| Metric | Current contract | Migration boundary |
| --- | --- | --- |
| RBP | `offline-spectral-v3` PSD, interpolated bands, 1-30 Hz denominator | primitive graph |
| Theta/Beta | IAPF-relative bands for logical Fz/Pz/Oz roles | executable composite adapter, explicit channel mapping |
| FAA | filtered F3/F4, 2 s paired epochs, 50% overlap, min 10 clean epochs | composite, paired quality |
| BrainBeat | legacy realtime 2 s Welch plus three-frame log-domain EMA warmup | formula and EMA shadow separately |
| IAPF | 3-30 Hz log10 1/f OLS excluding 7-13 Hz; residual Peak then COG | executable composite adapter |

No definition graph is allowed to silently change a composite metric into a
different static calculation. IAPF and Theta/Beta have separate adapter
evidence; FAA and BrainBeat still require their own future cutover evidence.

## Shadow evidence

`backend/app/eeg_core/official_algorithm_shadows.py` is deliberately
backend-only. The candidate implementations do not call production helpers
for the calculation being verified:

- RBP uses typed primitive composition against the legacy band integrals.
- FAA performs its own paired 2 s Hann FFT and alpha integration.
- BrainBeat performs independent inclusive integrations and separately models
  the log-domain EMA state machine.
- IAPF uses an independent least-squares fit and residual Peak/COG decision.
- Theta/Beta uses independent interpolated-boundary integration and does not
  reuse the BrainBeat ratio.

`backend/scripts/run_official_algorithm_shadows.py` persists finite comparisons
as `ValidationRun` records. It stores only recording identity hashes, sizes,
configuration digests, summaries, tolerances, and environment. It never stores
source filename or EEG samples. Example:

```powershell
cd backend
.\.venv\Scripts\python.exe scripts\run_official_algorithm_shadows.py <recording_id> --start-s 0 --window-s 30
```

Theta/Beta needs a logical `Oz` mapping. If the recording does not already
contain one, the command must receive an explicit raw source choice:

```powershell
.\.venv\Scripts\python.exe scripts\run_official_algorithm_shadows.py <recording_id> --posterior-source Oz
```

The script never treats `O2`, a third selected channel, or a similarly named
label as `Oz` without that explicit mapping. That is an engineering provenance
rule, not a claim that the two electrodes are interchangeable.

## Tolerances and cutover rule

Default numerical comparison tolerance is `rtol=1e-7`, `atol=1e-9` in the
metric's native unit. Unavailable/gate-failed values remain unavailable with a
reason; they are never converted to zero merely to create a comparison array.

Current outputs retain their existing implementation identity:

- offline spectral results: `offline-spectral-v3`;
- legacy realtime BrainBeat: `realtime-eegprocessor-v1`.

This change therefore establishes migration evidence, not a public result-path
cutover. A future change may switch one metric only after it has matching
synthetic and appropriately mapped local-data evidence for the exact pipeline
being replaced.
