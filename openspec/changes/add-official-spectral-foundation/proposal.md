# Official algorithm development plan

## Why

The platform already has several official algorithms and a migrated scientific
backend, but their history is spread across archived OpenSpec changes. This
change is the single active plan for the next official algorithm phase. It
records the complete delivery order, ownership boundaries, and acceptance
gates so implementation does not restart from an incomplete historical task
file.

## Scope

- Freeze the scientific contracts and ownership map before new algorithms.
- Complete reusable spectral primitives and the shared static/dynamic window
  execution path.
- Expose PSD and STFT as traceable official Runtime modules with structured
  multi-field outputs and artifacts.
- Keep official algorithm modules independently versioned, parameterized, and
  independently validated.
- Integrate results with Run provenance and the existing analysis APIs without
  moving scientific calculations into the frontend.
- Close the change only after full backend, reference, OpenSpec, and artifact
  verification.

## Out of scope

- Changing the existing `offline-spectral-v3` PSD or `spectrogram-v2` output.
- Adding UI controls or frontend-side scientific calculations.
- Making arbitrary user-defined algorithms executable.

## Source of truth

The plan is `design.md` and `tasks.md` in this change. The canonical long-term
contracts remain under `openspec/specs/analysis/`. Archived changes are
historical evidence only and must not be used as the next implementation
backlog.
