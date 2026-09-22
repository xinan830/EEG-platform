# Desktop recording review validation

Date: 2026-09-22

## Automated evidence

- Commit `18d4b64` unifies acquisition and review display settings, scaling, axes, geometry, and one-second major ticks.
- Commit `fd2d264` adds versioned causal-filter checkpoint export/import and contiguous review-chunk restoration.
- `dotnet test desktop-client/BrainPlatform.Desktop.slnx -c Release --no-restore` — **191 passed, 0 failed** on the final full-suite run.
- `backend/.venv/Scripts/python.exe -m pytest -q` — **243 passed, 0 failed**, with 2 dependency deprecation warnings.
- `openspec validate --all --strict --no-interactive` — **37 passed, 0 failed** before archive.
- `openspec validate --specs` — **30 passed, 0 failed** after synchronizing the recording-review main specification.
- `dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --no-restore -p:UseAppHost=false` — **0 warnings, 0 errors**.
- `git diff --check` — **passed** before archive.

The first desktop full-suite run had one timing-sensitive failure in
`HttpLiveFilterBridgeTests.FilterChange_KeepsActiveFilterPublishingWhileNewSessionWarmsUp`.
The focused rerun passed, followed by a clean 191/191 full-suite rerun. This is
recorded as test-flake evidence rather than concealed as a deterministic pass.

## Covered behavior

- Project recordings open in a dedicated review workspace and return to the owning project.
- Review reads bounded sample-major float64/V windows from immutable chunks.
- Sample-counter time remains authoritative; recorded gaps remain explicit and are never zero-filled or interpolated.
- Acquisition montage metadata remains immutable and separate from the current viewing montage.
- Compatible montage switching preserves absolute position and visible duration.
- Playback clock and waveform loading are decoupled, with stale work coalesced.
- Acquisition and review share display settings, scaling, axes, geometry, and one-second major-axis policy without sharing lifecycle state.
- Scientific filtering remains in the local Python service.
- Filtered source chunks use versioned causal checkpoints only across contiguous sample-counter ranges; gaps reset causal state.
- Incomplete cache entries and incomplete target frames are never exposed as complete results.

## Validation boundary

This is engineering validation, not clinical or diagnostic validation. It does
not establish calibrated physical paper speed, medical-device compliance, or
clinical equivalence.

A first random seek far into a long recording may still require sequential
checkpoint generation for all preceding contiguous samples under the selected
filter settings. Skipping that history would change causal output, especially
for the supported 0.01 Hz high-pass filter. Optimizing first-jump latency
therefore requires a separate bounded/background anchor-generation design.

Hardware-independent automated validation is complete. A manual WPF acceptance
run against a real amplifier and long recording was not performed in this
command session.
