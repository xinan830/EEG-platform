# Add WPF dynamic algorithm workflow

## Why

The backend already exposes dynamic official algorithm execution and the
canonical window states. The WPF client currently accepts only static runs,
so users cannot configure or inspect the dynamic contract from the main
product path.

## Scope

- Add dynamic mode selection for catalog entries that advertise `dynamic`.
- Expose backend-owned window and step options without duplicating scientific
  defaults in the client.
- Submit dynamic runs through the existing `AnalysisRunRequest` boundary.
- Render bounded backend-produced dynamic summaries and window states without
  recomputing scientific values in WPF.
- Reuse existing shared ComboBox, Button, and status styles.

This change does not alter algorithm formulas, backend window scheduling, or
the final visual redesign of the algorithm page.

