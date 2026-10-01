# Design

Peak Frequency owns result parsing, display state and a SciChart time trend under its own WPF module. The shared catalog retains recording selection, parameters, run execution and the existing playback cursor. The chart receives only backend frequencies in Hz; rejected or unavailable points are gaps. Peak PSD power is display-only evidence converted explicitly from V²/Hz to μV²/Hz.

## Shared-file change log

This is the list of common integration files touched to add one algorithm detail. It is evidence for a later boundary decision, not authorization for a broad refactor.

- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Catalog/AlgorithmListViewModel.cs`: call the algorithm-owned dynamic-window adapter after a completed Run; preserve seek position on a rerun.
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmDetailViewModel.cs`: instantiate the Peak Frequency detail model and connect it to the existing quality card.
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/QualityStatusViewModel.cs`: select the current algorithm through the shared `IAlgorithmQualitySource` contract. The quality card no longer encodes algorithm-specific priority branches.
- `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/IAlgorithmQualitySource.cs`: shared presentation contract for algorithm-owned quality state and failure details.
- `backend/app/services/run_analysis_executor.py`: include the already-computed structured failure in every dynamic scalar point. This is a generic serialization fix, not Peak Frequency science.

Existing shared detail XAML and analysis parameter controls already know about Peak Frequency; neither needs editing. The dynamic-window adapter is in a new algorithm-named partial file and is not counted as a modified common file.

`AlgorithmDetailViewModel` remains the explicit composition root for detail modules. Adding a quality-capable algorithm still requires one registration entry there, but its quality fields and state transitions stay inside the algorithm module. The remaining central additions are therefore registration points rather than algorithm-specific quality logic.

The same boundary now applies to run completion and input validation: each
algorithm module exposes an `IAlgorithmRunResultHandler`, while the catalog
only resolves the handler, submits the common request, and persists shared run
state. Frequency-band, FAA channel, STFT minimum-range, and notch validation
therefore live beside the corresponding algorithm module.
