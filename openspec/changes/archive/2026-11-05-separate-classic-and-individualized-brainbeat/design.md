## Context

See proposal.md - Why. Current execution already provides paired Fz/Pz spectra, four-second Welch PSDs with 50% overlap, exact-boundary trapezoidal band integration, shared quality checks, official catalog manifests, immutable Run artifacts, and backend-owned on-demand windows. The catalog spec currently names the old IAPF-relative calculation as BrainBeat, so the scientific identity must move together across these boundaries.

## Goals / Non-Goals

**Goals:**
- Make the public `brainbeat` identity mean fixed-band absolute-power ratio.
- Keep the current IAPF-relative formula available as the explicitly named `brainbeat_individualized` variant.
- Make catalog, Official Run, on-demand review, result evidence, and cache identity agree on which formula ran.
- Preserve historical stored results without adding retired calculators or data conversion.

**Non-Goals:**
- Change the shared filter or Welch estimator, quality thresholds, montage/reference behavior, or other algorithm formulas.
- Claim the platform implementation reproduces every detail of Holm et al. until the article methods are independently checked.
- Add cross-window smoothing, IAPF fallback to classic BrainBeat, or automatic channel-role inference.

## Decisions

1. **Use separate current algorithm IDs.** `brainbeat` becomes classic; `brainbeat_individualized` owns the existing IAPF-relative formula. This is clearer than a hidden mode under one ID because the formula, IAPF dependency, units of intermediate evidence, and interpretation are materially different. A single ID with a formula selector was considered but rejected because it makes catalog selection and historical comparison easier to misread.

2. **Advance the classic scientific identity and preserve stored bytes.** Bump the scientific version, Definition identity, implementation identity, and normalized config/cache identity for the new `brainbeat` calculation. Register the individualized formula as a new current ID. Do not run an old calculator when reading old Runs; the existing canonical historical payload remains immutable and carries its original persisted identity.

3. **Use exact bands and absolute powers for classic.** Integrate Fz 4–8 Hz and Pz 8–12 Hz separately with the established boundary-interpolated trapezoidal primitive. Do not divide either by broadband power. Keep internal spectral values in V²/Hz and integrated powers in V²; expose intermediate units explicitly and the final dimensionless ratio. Reuse the existing `1e-20` numerical floor only for internal stability if needed, but return unavailable when the denominator is non-finite or non-positive rather than presenting a synthetic value.

4. **Keep the shared spectral path.** Both variants use the paired source order Fz then Pz, configured continuous zero-phase preprocessing, 1–50 Hz fourth-order Butterworth bandpass, optional 50/60 Hz Q=30 notch (default 50 Hz), four-second Hann Welch segments, 50% overlap, density scaling, mean segment averaging, and the 75% clean-segment gate. No resampling is introduced. The high-pass output axis remains 1–30 Hz for both BrainBeat variants.

5. **Declare 20 seconds / one second as both dynamic defaults.** Support 10, 20, and 30-second complete trailing windows, with a 10-second minimum and no numeric warm-up result. This follows the stated stability-first recommendation and avoids the current 5-second review versus 10-second Run mismatch. Window length remains configurable and does not change the Welch segment length.

6. **Treat article confirmation as an evidence gate for the literature claim.** Implement the user-specified 4–8 and 8–12 Hz classic bands as the target contract. Before describing it as an exact Holm reproduction, verify the article's methods for band edges, absolute versus relative power, epoching, and estimator. If the full method cannot be accessed, record that limitation and describe the implementation as a fixed-band classic-style variant rather than inventing undocumented paper parameters.

7. **Put variant choice in catalog and review configuration.** Official Run uses the selected ID. Review presents a clear variant choice, serializes the matching ID and policy, and renders backend-returned formula identity, actual range, quality, and result. No formula or filtering code moves into WPF.

## Risks / Trade-offs

- [Historical ID ambiguity] Old rows may contain `algorithm_id=brainbeat` but a prior scientific version. → Historical readers must display the stored version/evidence and must not infer classic semantics from the ID alone.
- [Article method details unavailable] User-supplied bands may differ from exact paper settings or associated estimator details. → Verify the primary methods before claiming exact literature replication; retain source and version provenance.
- [20-second response delay] Complete-window-only output delays the first value relative to the current 4-second warm-up. → Show pending/insufficient data with the actual range and keep playback responsive.
- [Cross-channel ratio interpretation] Absolute Fz Theta/Pz Alpha power retains channel gain/reference sensitivity. → Preserve explicit montage/reference provenance and describe the output as a ratio, not a normalized or device-invariant load score.

## Migration Plan

1. Add the classic and individualized contracts, catalog identities, and regression tests before changing the active `brainbeat` manifest.
2. Switch `brainbeat` to the versioned classic formula; register the existing relative formula under `brainbeat_individualized` and update its own identity.
3. Update request validation, review selection, and presentation to serialize and show the selected ID and its 20-second/one-second policy.
4. Verify old Run records are read without rewriting or calculation fallback; new Run and on-demand results must have distinct cache identities for the two variants.
5. Rollback consists of reverting application code while retaining immutable historical rows and artifacts. Do not migrate stored outputs or alias one variant to the other.

## Testing Strategy

- Use synthetic two-channel signals with known in-band powers and independently vary out-of-band power to prove classic BrainBeat uses absolute band powers rather than relative denominators.
- Test the IAPF-dependent variant separately, including valid peak/COG, unavailable IAPF, and exact-boundary integration behavior.
- Test role order, missing/duplicate channels, sample bounds, notch settings, quality rejection/null semantics, and 20/21/22-second window bounds.
- Compare Official Run and on-demand output for identical source bytes, config, and actual window.
- Verify catalog schema, desktop serialization, review variant labels, historical Run retrieval, cache identity separation, OpenSpec strict validation, focused backend/Desktop checks, and diff whitespace.
- Validate the Holm methods against the primary article source before using the phrase exact reproduction; scientific validity and clinical validity remain outside engineering test claims.
