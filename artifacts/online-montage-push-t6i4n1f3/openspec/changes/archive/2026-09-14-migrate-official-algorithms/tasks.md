## 1. RBP shadow migration

- [x] 1.1 Define the immutable RBP definition and calculate old/new shadow evidence.
- [x] 1.2 Verify synthetic PSDs, frozen spectrum golden values, channel order and quality failure semantics.

## 2. Remaining official algorithms

- [x] 2.1 Define and shadow FAA with its paired epoch quality semantics.
- [x] 2.2 Define and shadow BrainBeat without conflating offline and legacy realtime EMA semantics.
- [x] 2.3 Define IAPF as a composite node retaining 1/f fit, peak/COG and lock state.

## 3. Cutover evidence

- [x] 3.1 Persist comparisons, document tolerances and only switch a metric after its complete shadow suite passes.
- [x] 3.2 Pass all gates, archive the change, and preserve legacy result identity.
