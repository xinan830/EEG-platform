# add-psd-all-band-topomap

PSD review topomap supports concise band labels and simultaneous five-band display.

## Color normalization validation — 2026-11-05

The shared renderer now normalizes against the actual nonzero range. Constant fields retain equal legend bounds and use the midpoint color; values outside locked bounds still saturate at the endpoints. Backend values and algorithm versions are unchanged.

- Original renderer: 7 new regression cases failed; 9 existing rendering cases passed.
- Fixed renderer: 54 focused WPF cases passed (`TopomapRenderingTests`, `PsdReviewSelectorTests`, `PsdTopomapDensityTests`, `ReviewPsdAnalysisTests`, and `RbpReview*`). Tests build to `_temp/psd-color-scale-tests` to avoid the running desktop app's output.
- Backend: `test_psd_topomap_density.py`, `test_topomap.py`, and `test_file_size_policy.py`: 50 passed on Python 3.12.13. Two third-party TestClient deprecation warnings remain.
- Strict OpenSpec validation and `git diff --check` passed. The renderer (261 lines) and rendering tests (225 lines) remain below the 400-line target.
- Existing WPF dependency compatibility warnings remain. No full-suite run or live desktop operation was performed. Actual recording/channel payload equivalence was not revalidated; the rendering regression compares proportional grids and bounds. Visual acceptance remains with the user after rebuilding/restarting WPF.

## Montage projection repair — 2026-11-05

Validated average-reference outputs such as `F3-AVG` now use the saved position of their positive electrode. Channel-referenced and specified-pair outputs remain spatially unavailable because the current contract does not define a unique site for their differential value. The rule is shared by PSD and RBP topomaps and does not change algorithm values or reference semantics.

- Added regression coverage for average-reference projection and continued exclusion of both bipolar formula kinds.
- `test_topomap.py`, `test_psd_topomap_density.py`, and `test_file_size_policy.py`: 53 passed; strict OpenSpec validation passed; no WPF runtime or real recording was used.
