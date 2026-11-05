# Design

Apply an algorithm-specific default in the Official Run request model for dynamic FAA, matching the manifest's 20-second default. Keep the existing shared 10-second default for other algorithms and preserve any explicit `dynamic_window_s` supplied by callers. This fixes the request-boundary mismatch before runtime scheduling and provenance snapshotting.

No quality-ratio threshold is introduced: no FAA-specific threshold is currently established, and selecting one would alter scientific result availability. Existing minimum of 10 clean paired epochs remains in force.

The existing `backend/tests/test_official_algorithm_runs.py` is over the project's 400-line soft limit and has multiple algorithm concerns. This change avoids adding another integration scenario there; the small configuration test verifies omission and explicit override at the request boundary, while existing runtime tests cover FAA window execution. A future focused test-file extraction should be evaluated by test ownership rather than performed as incidental cleanup here.
