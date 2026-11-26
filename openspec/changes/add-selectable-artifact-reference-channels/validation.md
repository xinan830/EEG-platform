# 专用眼电 / 心电参考通道验收记录

日期：2026-11-26。范围：离线回溯中选择记录自带的 EOG / ECG 参考，接通 ICA 审查、确认、科学预览与算法；未提交或推送 Git。

## 已实现

- 科学预处理第 05 步新增 EOG 多选与 ECG 单选，来源为当前录音可验证的原始电压通道；ECG 用于 ICA。选择变更清除待审查/已确认模型、科学对比与坏段报告，旧异步响应不能覆盖新输入。
- WPF / EDF / BDF 原始辅助读取与 EEG 读取分别声明；内部 V/float64、绝对样本坐标、原始列顺序明确。EDF/BDF 原始物理单位与原生采样率由已安装 pyedflib 检查，避免 MNE 把计数通道推断成 V。
- 显式参考先于坏导插值和导联重参考移出 EEG 输入；CAR 池、插值供体、EEG ICA 拟合、地形图及算法输出不再包含所选参考。专用参考仍可从原始源单独读取，辅助数据分配纳入调用方资源预算，异常释放已分配映射文件。
- ICA 只拟合 EEG；EOG/ECG 仅追加到评分数据中，证据同时显示实际眼电与心电参考及评分。确认后的组件剔除复用同一模型指纹。EOG 回归 / SSP 支持显式 EOG。留空沿用原自动/代理策略，代理仍由现有选项控制。
- 共享算法入口通过后端实际读取器确认有效 EEG 通道列表，PSD、RBP、复合数值面板及其他注册面板统一初始化。桌面不复制导联公式，也不按名称后缀猜依赖。
- 没有修改普通彩色波形显示滤波；已有两个波形叠图工作区改动保留，不归入本功能验收。

## 验证

- 后端 16 个相关模块：143 passed，0 failed（27.92 s）。覆盖专用参考、去伪迹、坏导/坏段、科学预处理、STFT、预览、导入、读取及既有修复。最终增加的真实 ICA 成分剔除数值变化断言另跑 1 passed。
- 桌面相关范围：253 passed，0 failed（18 s），含 ReviewPreprocessing、HttpArtifactReference、IcaPreview、BackendRecordingAccessSession、MontageDisplayProjector、ReviewArtifactReport、OfflineAnalysisSelection、RecordingReviewViewModelTests、ReviewSpectralValuesTests。
- 专用参考测试直接写入合成 WPF、EDF 与 BDF 文件，实际调用发现、ICA、预览、坏段、通道解析、频谱、PSD/RBP 全量及按需 API。验证两条算法路径能完成并保持 EEG 通道顺序。
- 额外验证：导出别名到原始电极的映射、CAR 与坏导同时排除后的缓存命中、多 EOG SSP、仅 ECG 的 ICA、参考 NaN 断层、非电压/不存在/角色重叠失败、分配失败清理。
- WPF STA 渲染与绑定检查通过；发现并修复重新加载选项时 ECG 显示空白的问题，确认选择内容和 SelectionBoxItem 均保持。截图位于忽略目录 `.artifacts/artifact-reference-dialog.png`。
- 文件规模策略、`git diff --check`、`openspec validate add-selectable-artifact-reference-channels --strict` 通过。业务文件超 400 行的职责评估登记在 `docs/code-size-policy.json`。

## 既有失败与验收边界

`test_algorithm_runtime_architecture.py`：21 passed / 3 failed，失败项为 brainbeat 的 window_s 最大值、custom_frequency_inspection.py 的具体科学模块导入、live_rbp_algorithm.py 的具体算法实现导入。对应文件本次无差异，没有修复或隐瞒这些既有失败。

没有跑全量后端/桌面所有测试，也未做真实专用 EOG/ECG 电极硬件和生理识别准确率验收。此前提供的私人 EEG 录音没有独立 EOG/ECG，无法用于证明专用眼电/心电识别效果。合成数据/API 通过不等于临床有效性验证。

此版本要求 EDF/BDF 电压通道使用共同的原生采样率；混合采样率明确返回对齐错误，需先显式重采样，不会静默接受 MNE 隐式对齐。前后端契约需一起部署，旧 ICA 确认身份失效后需要重新审查。
