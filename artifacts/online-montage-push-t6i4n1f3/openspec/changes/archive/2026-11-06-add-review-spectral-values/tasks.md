## 1. Existing Result Contracts

- [x] 1.1 Verify the existing PSD/RBP adapters use matching playback windows and shared Welch settings, and verify PSD projection plus RBP absolute/relative result units and quality contracts.
- [x] 1.2 Expose the existing backend RMS helper through the current RBP result-projection contract and test valid, missing and invalid absolute-power values without changing RBP algorithm output arrays.

## 2. Review Presentation

- [x] 2.1 Add the review button and right-side numeric table with canonical band selection, montage-order rows, four requested columns and explicit units.
- [x] 2.2 Bind both existing dynamic adapters to the shared playback cursor; preserve window settings, source quality and provenance, and suppress mismatched or stale result pairs.
- [x] 2.3 Add focused desktop checks for selected-band values, source fidelity, units, window/montage changes and unavailable states.

## 3. Verification

- [x] 3.1 Run relevant backend and desktop tests, OpenSpec strict validation and diff checks; record that manual playback acceptance remains to be performed.

## 4. Remove Presentation Work From Numeric Updates

- [x] 4.1 Give map and numeric consumers one shared result-to-scalar adapter, expose a typed scalar-only window resource, and verify it never prepares coordinates or interpolation grids.
- [x] 4.2 Switch numeric consumers to scalar contracts and retain observable table rows across updates, unavailable transitions and band changes.
- [x] 4.3 Verify delayed scalar loading never gates waveform playback or seek, preserve lifecycle/source checks, and record focused tests plus measured projection cost.

## 5. Readable Row Inspection

- [x] 5.1 Replace raw quality JSON with readable summaries and reuse the shared bounded tooltip; verify compact displayed units, reasons, placement binding and wrapping without changing computation or table styling.
- [x] 5.2 Remove the aggregate quality tooltip from the review RBP stacked-history container, preserving chart point inspection and quality data; compile and run relevant RBP regressions (36/38 passed; two standalone unloaded-chart assertions failed, recorded in validation).
