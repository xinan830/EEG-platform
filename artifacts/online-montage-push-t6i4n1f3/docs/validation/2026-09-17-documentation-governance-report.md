# Documentation Governance Cleanup Report

Date: 2026-09-17

## Scope

This report covers repository documentation governance only. It changes no EEG computation, API, stored data, unit, time, channel, quality, provenance, or frontend behavior.

## Completed

- Added `docs/README.md` to define current documentation roles and precedence.
- Moved 54 historical ad-hoc change notes to `docs/archive/change-notes/` and added an archive index.
- Removed 11 unreferenced `docs/superpowers/` planning drafts. Their completed decisions remain in `openspec/changes/archive/`; Git history preserves recovery.
- Updated the engineering contract and algorithm implementation guidance so future contract-level work starts with one OpenSpec change.
- Retained current OpenSpec specifications, archived OpenSpec changes, validation reports, roadmap, and legacy real-time background documentation.
- Confirmed the consolidated `brain-research-platform` skill remains discoverable; deprecated skill directories contain no skill definition and are not discoverable.

## Verification

- `openspec validate clean-up-documentation-governance --strict --no-interactive`: passed.
- Repository search found no remaining external reference to removed `docs/superpowers/`, `docs/changes/`, or the deleted change-note template.
- `git diff --check`: passed; existing line-ending warnings did not report whitespace errors.

## Residual Notes

- Empty physical directories can remain locally after file removal, but Git does not track them and they are not documentation or skills.
- `ANT-Amplify/` is an untracked third-party SDK and sample-data bundle. It is out of scope for this change and must not be added to Git without a separate vendor and licensing decision.
