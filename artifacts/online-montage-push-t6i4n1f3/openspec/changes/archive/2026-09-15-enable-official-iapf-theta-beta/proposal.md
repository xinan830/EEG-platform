# Enable official IAPF and individualized Theta/Beta

## Why

IAPF and individualized Theta/Beta already have isolated calculation modules
and shadow evidence, but the official catalog still marks them as
shadow-only. Users therefore cannot run the validated official algorithms
through the traceable Run pipeline.

## What Changes

- Add a backend official-algorithm Run execution path for `iapf` and
  `theta_beta`.
- Support static and dynamic windowed execution using the existing offline
  spectral contract and persisted Run/Artifact/provenance lifecycle.
- Require explicit saved logical channel mapping for individualized
  Theta/Beta; never infer `Oz` from `O2` or channel position.
- Mark only IAPF and Theta/Beta as available and runnable in the official
  catalog, with Chinese-readable labels and result metadata.
- Keep FAA, BrainBeat, and RBP availability unchanged.

## Non-Goals

- No changes to `offline-spectral-v3`, IAPF math, Theta/Beta boundaries,
  quality gates, units, or historical results.
- No real-time device support, EMA state machine, FAA cutover, or generic DAG
  representation of composite algorithms.
