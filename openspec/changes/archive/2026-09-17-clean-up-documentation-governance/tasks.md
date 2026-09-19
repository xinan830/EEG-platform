## 1. Documentation Governance

- [x] 1.1 Add the repository documentation index and archive index.
- [x] 1.2 Update the engineering contract and algorithm documentation guidance to name OpenSpec as the canonical change workflow.
- [x] 1.3 Move historical ad-hoc change notes into the documentation archive.
- [x] 1.4 Remove unreferenced superseded `docs/superpowers/` drafts.

## 2. Skill Consolidation

- [x] 2.1 Remove the two deprecated legacy skill definitions and confirm no obsolete skill is discoverable.
- [x] 2.2 Verify `brain-research-platform` remains the only EEG-platform skill.

## 3. Verification And Archive

- [x] 3.1 Confirm removed draft paths have no remaining repository references.
- [x] 3.2 Run `openspec validate clean-up-documentation-governance --strict --no-interactive`.
- [x] 3.3 Run `git diff --check`, archive the change, then run `openspec validate --all --strict --no-interactive`.
