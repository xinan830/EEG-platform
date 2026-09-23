# Desktop Client Module Boundaries

This document defines the stable source layout for `BrainPlatform.Desktop`.
Directory moves are structural changes only: acquisition timing, raw-recording
contracts, display filtering, review caching, and scientific computation must
retain their existing behavior.

## Placement Rules

- `Modules/<Feature>` owns feature-specific domain types, stores, services,
  view models, and views.
- `Modules/Acquisition` owns acquisition orchestration, session state, raw
  storage, and live display integration. It does not own vendor SDK drivers.
- `Modules/Devices` owns device capability contracts, driver registration, and
  vendor-specific SDK implementations. Device discovery remains coordinated by
  the acquisition session owner.
- `Modules/Channels` owns source-channel configuration and validation.
- `Modules/Montages` owns display montage definitions, persistence, and
  validation. A montage must remain separate from recorded raw channels.
- `Modules/Projects`, `Modules/Events`, and `Modules/Review` own their
  respective project, annotation, and review concerns.
- `Shared` contains only cross-module, non-business code: MVVM primitives,
  notifications, display calibration, reusable controls, dialogs, and styles.
- `Infrastructure` contains external-system boundaries such as backend HTTP
  clients and persistence adapters. It must not become a second domain layer.

## Prohibited Catch-All Locations

Do not add new root-level `Services`, `ViewModels`, `Models`, `Helpers`,
`Utils`, `Managers`, `Domain`, or `Configuration` folders. Place new code in
its owning module, `Shared`, or `Infrastructure` according to the rules above.

## WPF Migration Rules

Views are moved one feature at a time together with their code-behind and view
models. For each batch, update `x:Class`, code-behind namespace, resource
dictionary URIs, and composition references together. Do not combine a view
migration with UI redesign or behavior changes.

`App.xaml`, `MainWindow.xaml`, shared resource dictionaries, and the currently
validated acquisition/review rendering paths stay in place until their own
dedicated migration batch. WPF resource URIs and BAML compilation make these
areas higher risk than ordinary C# moves.

## Verification

Every migration batch must build the desktop client and run the desktop test
suite. Before finalizing the reorganization, run OpenSpec strict validation and
`git diff --check`. Physical hardware acceptance remains separate from unit
tests; structural refactoring is not evidence of device-stream correctness.
