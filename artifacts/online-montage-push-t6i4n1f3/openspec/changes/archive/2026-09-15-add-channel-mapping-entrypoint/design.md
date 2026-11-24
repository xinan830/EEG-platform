# Design

The importer has the only automatic pathway. It derives a mapping only if each
required logical role Fz, Pz, and Oz has exactly one source-label match under
case/whitespace normalization. The resulting mapping is persisted through the
existing recording mapping service, including optional exact F3/F4 matches.

If any required exact role is absent, no partial mapping is saved. The viewer
continues to use raw selected channels. The workbench exposes the existing
mapping form as a modal. When Oz is absent but O2 exists, the form preselects
O2 and clearly labels it as a suggestion; persistence still requires the user
to select the explicit save command. This preserves provenance for the
official Theta/Beta execution path.
