# Documentation Guide

This directory explains the project for people. It does not replace the current OpenSpec contracts.

## Source Of Truth

1. `openspec/specs/` defines current observable behavior, public contracts, scientific semantics, and failure behavior.
2. `openspec/changes/archive/` records why a completed change was made and how it was accepted.
3. `docs/validation/` records validation and reproducibility evidence.

When these sources conflict, current OpenSpec specifications take precedence. Historical documents never override code, tests, or a current specification.

## Current Reading

- `architecture/`: human-readable architecture decisions and module boundaries.
- `algorithms/`: processing, unit, timing, validation, and implementation context.
- `roadmaps/`: the current delivery roadmap.
- `validation/`: completed engineering validation reports.
- `工程开发契约.md`: project-wide engineering requirements.

## Historical Material

- `archive/change-notes/`: pre-OpenSpec-style change notes retained for debugging context only.
- Git history: removed draft plans and superseded documents remain recoverable by commit.

Do not add a second changelog, design-plan, or contract tree under `docs/`. Contract-level work begins with one OpenSpec change; update a human-readable document only when it helps explain the resulting current behavior.
