## Context

See proposal.md and the three delta specs. TBR already uses the shared spectral loader and band integration primitive, but its frequency definition is implicit. Catalog-driven Review reads the manifest policy, while Official Run requests currently default to a separate 10-second value.

## Goals / Non-Goals

**Goals:** Keep all numerical work in the existing backend module; make frequency mode and dynamic-window policy part of the validated, persisted configuration; keep Official Run and on-demand Review on the same contract.

**Non-Goals:** Change the shared spectral loader, quality thresholds, preprocessing, channel selection, Welch parameters, or result units. Do not add 1/f-corrected power, baseline IAF locking, or multi-channel IAF aggregation.

## Decisions

- Use `frequency_mode` with `standard` and `individualized` values. The existing execution `mode` remains `static` or `dynamic`. Standard is the default because it gives the unqualified TBR label its canonical 4–8/13–30 Hz meaning; individualized mode remains an explicit alternative.
- Keep both formulas in `compute_theta_beta` and call the shared `band_power` primitive. Only individualized mode calls the existing IAPF estimator. Validate each effective band against the frequency axis before integration; never move a requested edge to a different frequency.
- Include mode, optional IAPF, exact effective bands, and band powers in the existing evidence/calculation trace. The typed config already participates in Run and on-demand identities, so no new cache key layer is needed.
- Declare a TBR-owned 20-second minimum/default, 20/30-second window options, one-second default refresh, and no warm-up in the manifest. Normalize an omitted Official Run window to 20 seconds for TBR only, and reject explicit unsupported shorter windows at the TBR config boundary.
- Bind Official Run mode through the existing algorithm parameter/configuration pipeline. Bind Review mode in `ReviewScalarParameters`; a mode change already invalidates and recomputes the current Review analysis. Neither UI calculates bands.
- Bump the TBR scientific, implementation, and persisted Definition identities. Keep saved historical Run summaries readable; no database or result migration is required.

## Risks / Trade-offs

- [Defaulting new Runs to standard changes their numerical meaning relative to the previous individualized-only output] → Use a new scientific/implementation identity, persist `frequency_mode`, and leave historical results untouched.
- [Twenty seconds delays the first dynamic value and requires longer recordings] → Reject or mark insufficient ranges clearly; expose supported 30-second windows for users who choose them, without publishing partial numeric ratios.
- [The cited study used a different IAF estimation method and posterior channel set] → Describe this mode as the platform's IAF-relative TBR formula, not a complete reproduction of that study.

## Migration Plan

New TBR Runs use the new scientific version and explicit mode. Existing definitions and saved summaries remain available as historical records. Rollback restores the previous current identity for new Runs without rewriting prior results.

## Validation

Run focused TBR compute/config/dynamic and Official Run backend tests, WPF parameter serialization and Review mode-change tests, the WPF build, strict OpenSpec validation, and `git diff --check`. Do not run the repository-wide suite unless a focused failure points to shared behavior.
