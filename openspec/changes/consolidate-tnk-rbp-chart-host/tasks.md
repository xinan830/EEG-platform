## 1. Panel contract

- [x] 1.1 Define one logical chart panel per RBP channel, preserving channel order, labels, percent units, and empty/quality state.
- [x] 1.2 Keep focused tests for panel lifecycle, stacked series order, cursor value lookup, and late-window append/rebuild selection.

## 2. Desktop chart-host implementation

- [x] 2.1 Keep `TnkAnalysisPlotView` to one `SciChartSurface` per `TnkPlotPanel`, with one axis/cursor/legend lifecycle and one coalesced refresh boundary.
- [x] 2.2 Preserve append planning, history reconciliation, empty-state text, cursor throttling, zoom/pan behavior, and performance diagnostics per panel.
- [x] 2.3 Bind `TnkDetailView.xaml` to one plot view per active panel and remove the obsolete shared-surface projection path.
- [x] 2.4 Keep native RBP stacked rendering and lightweight legends; do not represent stacked bands as offset line series or 20 checkboxes.
- [x] 2.5 Schedule panel refresh below input/render priority and unsubscribe hidden or unloaded panels.

## 3. Verification and documentation

- [x] 3.1 Add or update desktop behavior tests covering tab changes, five-channel RBP ordering, one-host ownership, cursor updates, and disposal/unsubscription.
- [x] 3.2 Compile the affected desktop project and run only the focused desktop tests; record any unavailable environment checks.
- [x] 3.3 Run `openspec validate "consolidate-tnk-rbp-chart-host" --type change --strict` and confirm the change artifacts are complete.
- [ ] 3.4 **Pending manual WPA/UI run:** update the relevant architecture/performance evidence with measured before/after UI-thread and chart-host counts; state clearly that this is engineering validation, not clinical validation.
