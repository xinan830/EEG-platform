## Boundary

RBP is the first migration because its frozen `offline-spectral-v3` PSD,
boundary-aware integration and 1-30 Hz denominator already have parity-tested
primitives. FAA, BrainBeat and IAPF remain old-path outputs while their distinct
quality/window/real-time semantics are specified and shadowed separately.

RBP shadow tolerance is `rtol=1e-7`, `atol=1e-9` in ratio units. A failure is
evidence, not a fallback trigger. Shadow output never changes API payloads.
