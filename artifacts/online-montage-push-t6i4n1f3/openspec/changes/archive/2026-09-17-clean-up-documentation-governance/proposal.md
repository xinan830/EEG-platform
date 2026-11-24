## Why

The repository contains three overlapping documentation histories: current OpenSpec contracts, completed OpenSpec changes, and unstructured implementation notes. This makes it unclear which source governs current behavior and increases the risk that a future change follows an obsolete plan.

## What Changes

- Establish `openspec/specs/` as the single source of truth for current observable behavior and scientific contracts.
- Retain completed OpenSpec changes and validation reports as immutable engineering history.
- Move historical ad-hoc change notes into a clearly labeled documentation archive.
- Remove obsolete `docs/superpowers/` implementation drafts after confirming they have no external references and are superseded by archived OpenSpec changes.
- Add a concise documentation index and revise the engineering contract so future changes use OpenSpec rather than creating a second changelog system.
- Remove the two deprecated legacy skill definitions while retaining the consolidated `brain-research-platform` skill. Empty local directories without a `SKILL.md` are not discoverable skills.

## Capabilities

No capability requirements change. This is a documentation and repository-governance refactor only; `.openspec.yaml` opts out of spec deltas.

## Impact

Affected areas are repository documentation, OpenSpec workflow guidance, and local Codex skill discovery. No API, stored result, scientific calculation, unit, time, channel, quality, or provenance behavior changes.
