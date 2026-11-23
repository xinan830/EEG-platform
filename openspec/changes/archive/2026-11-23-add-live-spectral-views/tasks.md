## 1. Implementation and verification

- [x] 1.1 Register realtime PSD, STFT and topography projections using shared review computations.
- [x] 1.2 Connect acquisition controls and shared review charts to current live results.
- [x] 1.3 Verify parity, rolling delivery, error handling, desktop build and OpenSpec.
- [x] 1.4 Start live RBP and STFT chart history at the sample-counter-derived activation time while preserving rolling computation windows; clip the first STFT cell to activation time and verify chart axes, pre-activation exclusion, and acquisition session time conversion.

Validation: focused STFT, review heatmap and live spectral presentation tests passed (62 tests); strict OpenSpec validation passed. Hardware rendering remains unverified.

后端相关测试通过 204 项，桌面采集和图表相关测试通过 273 项。包含回溯的桌面组合测试通过 699 项，复现 3 项已有回溯基线失败。尚未用真实放大器进行桌面图表和长时间采集验收。
