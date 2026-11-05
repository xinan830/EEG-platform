## Context

Review currently binds one algorithm at a time. PSD returns ordered channel-by-frequency values; RBP returns per-channel band powers, and its quality policy differs from PSD. Both default to ten-second dynamic windows and four-second Welch segments with 50% overlap, subject to catalog settings.

## Goals / Non-Goals

**Goals:** Independently request the existing two algorithm windows and present their results with provenance and quality; compute RMS in the backend from RBP power.

**Non-Goals:** Change PSD/RBP computation or raw data, claim clinical validation, or infer missing results.

## Decisions

- Keep two independently owned dynamic bindings and pair only results with identical recording, sample window, channel order, montage and compatible configuration. Show each source's status and suppress misleading comparisons when mismatched. Reusing the visible chart projections would lose channel data and cannot run both sessions.
- Present each channel's band powers and expandable frequency/value table for the selected band. PSD samples remain unchanged; no average or peak is substituted. Display units and interval explicitly.
- Compute RMS as sqrt of valid, nonnegative RBP absolute power in uV^2, yielding uV, in a backend-owned projection. Preserve null and failure states. WPF scientific computation is prohibited.
- Reuse cancellation, latest-target scheduling and session release semantics; keep the table isolated from unrelated PSD/topomap and montage edits. Tests cover source fidelity, mismatch, unit conversion, cancellation and runtime UI integration.

## Risks / Trade-offs

- Two independent requests add backend load; use the existing bounded dynamic schedulers and cancel obsolete requests.
- PSD and RBP have different quality policies; show independent status and do not imply their values used identical accepted samples.
- Source files above the size threshold need documented review; keep new responsibilities separate.

## Migration Plan

No stored data migration. Roll back the panel and optional projection together; existing recordings and algorithm output remain usable.
