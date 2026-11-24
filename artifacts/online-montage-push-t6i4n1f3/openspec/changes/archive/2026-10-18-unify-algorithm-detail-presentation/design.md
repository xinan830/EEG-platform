# Shared Presentation Boundary

## Ownership

AnalysisStyles is the single definition of the card border and title style. The
four existing views remain in Algorithms/Views/Shared because their content binds
to algorithm-detail context and quality/provenance projections. Moving their files
alone would not make those contracts algorithm-neutral.

The offline review uses AnalysisPanelShell/AnalysisDockHost from the previous
change. It does not reuse the independent detail-page dynamic playback controls,
which would risk creating another playback clock. Charts and scientific policies
remain module-owned.

## Migration

Outer frames use AnalysisCardBorderStyle; provenance retains its existing 20/16
padding override. Headers use AnalysisCardTitleStyle. Local explicit dictionary
imports allow each view to be created independently in WPF tests and design tools.
The shared definitions use named keys, not implicit Border or TextBlock styles,
so unrelated screens are not restyled.

This is deliberately a visual migration, not a quality-contract refactor. Existing
badges, reason disclosure, progress and dynamic rows retain their bindings.

## Verification And Remaining Work

Check standalone resource resolution, frame/title overrides, existing execution
command binding tests, full desktop tests, Release build, strict validation and
diff whitespace. Real scientific-chart screenshots remain a separate GUI acceptance
step. Body/table styles and typed review diagnostic adapters are not part of this
round; they should be assessed by semantic compatibility rather than file location.
