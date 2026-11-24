## Context

See proposal.md. Nine current Review IDs include both BrainBeat variants. Existing panel callbacks already group initialization, request building and projection; `MainWindow` separately builds the adapter list. XAML repeats per-algorithm template triggers, and the ViewModel repeats scalar/title mappings.

## Goals / Non-Goals

One module-owned registration list associates ID, title, panel kind, parameters, presentation callbacks and adapter factory. Keep explicit named commands and controls as the existing WPF-facing interface. Scientific calculation and playback state machines remain untouched.

No backend changes, runtime plugin discovery, generic scientific calculators, dynamic UI generation, compatibility result readers or full-suite cleanup.

## Decisions

- Extend the existing panel registration and move its construction to a focused Review factory. The ViewModel creates default coordinators from those same registrations using the injected algorithm client. Retain the existing coordinator injection used by focused tests and direct result attachment.
- Registration creates and checks its adapter's algorithm ID before wrapping it in the existing coordinator. A mismatched adapter is a composition failure, not a fallback.
- XAML selects among its five explicit templates by the registration's strongly typed panel kind; named menu/toolbar commands remain and are checked against the registered IDs. Scalar parameter/presentation and panel title lookup use the active registration.
- Subscribe/unsubscribe parameter changes by registered configuration objects, ensuring one lifecycle owner. Keep specialized topomap and TNK subscriptions.
- Rejected alternative: reflection/plugin discovery or generating all XAML controls from descriptors. That adds infrastructure without improving scientific ownership.

## Risks / Trade-offs

- Changing bindings may hide a panel → build BAML and test XAML entry/template coverage plus active registration selection.
- Shared scalar display may accidentally reuse wrong input roles → test all scalar registrations and both BrainBeat identities/configurations.
- Existing delegate coordinators support direct historical result attachment without an algorithm provider → preserve that injection path; validate adapter identities only when creating registered coordinators.
- Oversized existing files → move panel composition into a cohesive file, record actual file sizes and retain high-risk split plans. The shell shrinks from 562 to 550 lines; correct its stale 540-line ledger without adding source to it.

## Migration Plan

No persisted data ownership or API changes. Replace the desktop composition in one change, run focused registration/adapter/lifecycle/identity tests and desktop compilation, then strict OpenSpec and diff checks. Rollback is reverting the desktop and documentation diff. Full-suite testing and manual GUI acceptance are not claimed; user requested no GUI operation.
