## 1. ANT location contract

- [x] 1.1 Add regression coverage for resolving ANT system profile labels, preserving REF/GND separation, and retaining unavailable-location behavior.
- [x] 1.2 Attach resolver results to the generated ANT channel configuration without changing order, labels, display selection, or channel kinds.
- [x] 1.3 Surface resolver failures as explicit status while keeping the channel configuration usable.
- [x] 1.4 Generate head-map coordinates through MNE's `plot_sensors(kind="topomap", sphere="eeglab")` projection, return them with the location snapshot, and consume them without desktop XYZ projection.

## 2. Montage direction integration

- [x] 2.1 Verify newly created montage profiles snapshot ANT channel locations and formula direction remains a projection, not separately stored state.
- [x] 2.2 Run focused WPF tests, OpenSpec strict validation, and `git diff --check`.
- [x] 2.3 Verify anterior/posterior electrode placement and bounded map coordinates against MNE's topomap projection, then rerun focused backend/WPF tests and OpenSpec strict validation.
