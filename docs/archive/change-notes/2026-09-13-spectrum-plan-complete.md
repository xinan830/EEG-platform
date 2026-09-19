# Spectrum plan completion

The guide-alignment plan is implemented through the planned P0/P1/P2 scope:

- shared active analysis range and explicit range commit behavior;
- `offline-spectral-v3` mathematics separated from `spectrogram-v2` contract;
- linear and dB Spectrogram power separated, with backend-only conversion;
- center-time Spectrogram bins, matrix metadata and per-window quality records;
- golden PSD/Band Power/RBP regression coverage;
- ECharts Line (PSD), 100% stacked Bar (RBP) and Heatmap (Spectrogram);
- dynamic PSD and dynamic Spectrogram serialized refresh behavior;
- read-only PSD and Spectrogram algorithm validation dialogs;
- repeatable synthetic performance benchmark at `backend/scripts/benchmark_spectral.py`.

The following remain intentionally outside this plan: clinical compliance, identity/permissions, PDF reporting, persistent analysis templates, multi-process cache and hour-scale viewport/LOD loading. They require separate product and deployment decisions.
