# Live Montage Coordinator Validation — 2026-11-23

The acquisition session now owns the transient montage revision. Waveform projection and live algorithm batches consume the captured snapshot at a sample-counter boundary; raw sample order, V units, timestamps, counters, recording writer input, and offline review remain unchanged. Live channel choices and topomap positions come from the selected montage. Reprojected live windows may start before the active boundary when their result carries the current montage revision; results from stale montage revisions are discarded. An unavailable mapping does not block raw sample upload.

- Focused desktop acquisition tests: 239 passed.
- Focused backend live-analysis/access tests: 29 passed.
- Isolated desktop Release build: 0 warnings, 0 errors.
- `backend/scripts/check_file_sizes.py`: passed; existing registered files above 400 lines are warnings only.
- `openspec validate --strict add-live-montage-coordinator`: passed before spec sync.
- `openspec validate --specs`: 91 passed, 0 failed; existing repository warnings remain outside this capability.
- Main specification synced with the delta; `openspec validate --strict add-live-montage-coordinator`: passed after sync.
- The change was archived as `2026-11-23-add-live-montage-coordinator`; its archived task checklist is complete. Aggregate `openspec validate --archived` reports 167 passing and 20 failed historical changes with incomplete task lists; this change is among the passing entries.
- `git diff --check`: passed; Git reported only LF-to-CRLF normalization notices.
- Hardware rendering and acquisition with the ANT device were not verified.

The acquisition coordinator is 843 lines and stays above the 600-line review threshold because one lifecycle owner currently serializes sample dispatch, capture, and montage selection; its reviewed split plan remains registered in `docs/code-size-policy.json`. The configured runtime is 465 lines and its application-boundary assessment is also registered.
