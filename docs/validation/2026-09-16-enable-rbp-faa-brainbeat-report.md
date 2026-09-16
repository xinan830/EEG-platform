# Official RBP And FAA Runtime Validation

Date: 2026-09-16

## Scope

This change enables traceable static Runs for official Relative Band Power
(RBP) and Frontal Alpha Asymmetry (FAA). BrainBeat remains deliberately
shadow-only because its published implementation is a stateful realtime chain
with IAPF lock and EMA warm-up, neither of which is represented by the current
offline Run lifecycle.

## Scientific Contracts

- RBP: frozen `offline-spectral-v3` PSD; Delta `[1,4)`, Theta `[4,8)`, Alpha
  `[8,13)`, Beta `[13,30]`; each band divided by the sum of the four bands.
- FAA: explicit raw source pair; paired 2 s Hann FFT epochs with 50% overlap;
  at least 10 clean pairs; output `ln(P_F4 Alpha)-ln(P_F3 Alpha)`.
- No frontend signal processing, PSD, power integration, ratio calculation or
  replacement of unavailable values was introduced.

## Evidence

- RBP Run test returns exactly four backend values and verifies their sum is
  one on a synthetic recording.
- FAA Run test verifies explicit `F3/F4` provenance, expected `ln(4)` value
  for a 2:1 amplitude synthetic pair, and rejection of duplicate source
  selection.
- Provenance test verifies paired epoch evidence is preserved for the debug
  panel.
- Catalog test verifies BrainBeat is listed but disabled, rather than hidden
  or marked runnable.

## Result

RBP and FAA are engineering-verified implementation paths, not clinical
validation. BrainBeat requires a dedicated realtime-session change before it
can be enabled without changing its scientific meaning.
