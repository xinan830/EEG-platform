## Context

See `proposal.md`. The current architecture already separates desktop display,
recording access, playback time, and backend science. This change cleans the
remaining boundaries without introducing a new framework or changing those
contracts.

## Goals / Non-Goals

**Goals:**

- Establish a written inventory of compatibility paths and their owners.
- Remove confirmed obsolete internal fallbacks from the current execution and
  WPF namespace paths.
- Keep one explicit composition root and make feature construction/lifecycle
  ownership visible.
- Give Views a narrow navigation boundary and keep modal ownership behavior.
- Keep generated output out of source review and Git.

**Non-Goals:**

- Do not remove support for historical WPF/BDF/EDF recordings.
- Do not remove current scientific algorithms because a source filename says
  `legacy`; Crimson and Zenlite remain current TNK calculations.
- Do not change algorithms, filters, quality gates, cache identities, units,
  time coordinates, channel mappings, public endpoints, or stored result
  formats.
- Do not add a dependency-injection container, service locator, or broad UI
  redesign.

## Decisions

### 1. Classify before deleting

Build a caller and data-use inventory for each candidate path. A path can be
deleted only when it is an obsolete internal fallback with no current caller,
XAML/BAML reference, persisted-data reader, API consumer, or provenance role.
Historical format adapters, current algorithms, and active adapters are kept.

**Alternative rejected:** delete every symbol or directory containing
`legacy`. That would remove current science and historical-file support.

### 2. Migrate WPF namespaces vertically

Move one feature slice at a time, updating `x:Class`, code-behind namespaces,
resource dictionary URIs, generated BAML references, and composition together.
Build and run desktop tests after each slice. Remove the old namespace only when
the repository has no remaining references.

**Alternative rejected:** leave aliases indefinitely. That preserves the
ambiguity this change is meant to remove.

### 3. Keep a single composition root

`App` remains the application root and `MainWindow` remains the shell. Feature
modules expose explicit composition methods/factories for their own services,
ViewModels, and navigation registrations. Shared state keeps one owner.

**Alternative rejected:** a new global container or service locator, which would
hide ownership and make tests depend on runtime registration order.

### 4. Use the narrowest navigation boundary

Views request navigation through an existing command or small module-owned
boundary. Code-behind may handle presentation-only events. `Window.GetWindow`
is retained only for assigning dialog ownership, never for feature navigation.

### 5. Contain generated output conservatively

Use the existing ignored temporary build root for isolated outputs. Add only
patterns verified to be generated. Before deletion, check for active build,
test, or desktop processes and never touch recordings, logs, databases, or
other user data.

## Risks / Trade-offs

- **[Risk]** A compatibility type is still referenced indirectly by XAML/BAML.
  **Mitigation:** search source, XAML, resource URIs and build output before
  removal; require a clean desktop build and tests.
- **[Risk]** Navigation refactoring changes dialog ownership or shell state.
  **Mitigation:** preserve modal-owner assignments and manually verify each
  changed workflow.
- **[Risk]** A cleanup is mistaken for scientific validation.
  **Mitigation:** run structural and regression checks only, and label them
  engineering validation rather than clinical validation.
- **[Risk]** Generated-output cleanup deletes useful evidence.
  **Mitigation:** delete only verified isolated outputs; preserve logs,
  recordings, databases, and validation reports.

## Migration Plan

1. Finish the inventory and freeze the deletion list.
2. Remove confirmed obsolete internal fallbacks and migrate one WPF namespace
   slice, validating after each slice.
3. Reduce composition-root ownership and migrate navigation callers.
4. Route and clean generated output.
5. Run the complete structural validation, update the architecture docs, and
   archive only after all tasks and the validation report are complete.

Rollback is a Git revert of the individual slice commit. No persisted data or
recording files are modified by this change.

## Testing Strategy

- Targeted backend tests for adapter registration and absence of the removed
  fallback.
- Desktop build and focused tests for each namespace/composition/navigation
  slice.
- Static searches for removed namespaces, direct feature navigation casts,
  generated paths and forbidden artifacts.
- OpenSpec strict validation and `git diff --check`.
- Manual checks for changed WPF navigation and resource/BAML loading.
