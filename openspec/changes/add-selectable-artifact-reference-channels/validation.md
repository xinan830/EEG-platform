# 专用眼电 / 心电参考通道验收记录

日期：2026-11-26。范围：离线回溯中选择记录自带的 EOG / ECG 参考，接通 ICA 审查、确认、科学预览与算法；未提交或推送 Git。

## 当前通道身份扩展验收（2026-11-26）

当前以本节及 design/spec 的身份规则为准；后文保留前两轮验证证据，其中“选择即剥离”等行为已被本轮取代。

- 第 05 步为每个原始电压源提供 EEG、专用 EOG、专用 ECG、待确认身份及保存按钮。评分勾选不改变身份；EEG 代理保持拟合/CAR/输出成员，专用及待确认源从科学 EEG 分离，ECG 评分要求专用 ECG。EOG 回归要求专用 EOG，EEG 代理可用于 ICA/SSP。
- 录音身份保存在托管存储的 `.channel-identities` 原子元数据文件，绑定源 SHA256；没有修改采集样本或原始清单，也没有数据库迁移。重新打开录音恢复身份。修改身份后旧 ICA、预览和坏段报告失效，未保存/角色冲突阻止科学请求。
- WPF 导出保留全部电压源，EDF/BDF 的 labels 元数据携带逐列身份；重新导入校验源哈希、列、角色和电压单位。已确认 EEG 的辅助源同样保留。未知/重复/非电压身份失败时不发布半成品。
- 统一源读取边界依据身份分区，科学基线与处理侧使用同一 EEG 群体；普通彩色阅图维持原通道读取/显示滤波。被身份分区移除的显示行由后端明确列为 excluded，桌面保留其他 EEG 对比并显示徽标，不伪造辅助波形。
- 预处理身份和会话包含角色指纹。会话建立/读取、准备返回、排队 Run 执行/发布以及动态窗口取消/发布检查身份变化，避免旧键发布新身份数据。仍按现有本地单用户服务边界工作。
- 后端 25 个相关模块：**261 passed，0 failed**（63.30 s）；最后补充会话创建期间身份变更复现后，身份/读取/批量/动态 worker/生命周期 6 模块重跑：**49 passed，0 failed**（31.34 s）。两次范围有重叠，不相加。覆盖真实 Infomax 自动/手选同 EEG 代理在 Raw/CAR、有无专用 ECG 时逐点等价，导出别名映射、身份 HTTP 保存、原始阅图不变、EDF/BDF 数值往返及队列失效。
- 桌面明确命名空间及契约范围：**628 passed，0 failed，1 skipped**（24 s）。包含身份控件/HTTP、未保存阻断、进行中 ICA 取消、科学对比缺失行、Review 面板和导联生命周期；跳过项需配置真实录音 HTTP 导出环境。
- 扩大到含所有名称中带 Review 的测试时，另发现 2 项未触及的 RbpResultPreviewTests 失败；单独复跑仍是 11 passed / 2 failed。两项在未加载的 RbpChart 上断言 5 条序列，但当前未加载状态尚未 Rebuild，实际 0 条；本轮未修改该控件或测试，也未把这次扩大运行宣称全绿。先前提示断言导致模拟请求未释放的测试已用 finally 释放，并在 628 项范围通过。
- 架构测试：**21 passed / 3 failed**，仍为下文列出的 brainbeat 最大窗口值、custom_frequency_inspection 和 live_rbp_algorithm 依赖边界；对应文件本轮无改动。
- 文件规模已按物理行数复评登记；RecordingService 634 行属于高风险门面，新增身份规则独立在 recording_channel_roles，下一拆分边界为目录/注册持久化。严格 OpenSpec 和差异空白检查通过。未提交、推送或修改 Git 暂存区。

限制：没有运行全量后端/桌面所有测试；未做真实专用 EOG/ECG 硬件或生理识别准确率验收。前后端需同步更新，已有 ICA 必须重新审查；备份录音时需保留托管身份元数据，EDF/BDF 往返需同时保留匹配 labels 文件。

## 原始实现证据（已被本轮身份规则更新）

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

## 前一轮五项一致性修复证据（2026-11-26）

- 源读取边界在剥离专用参考时，同时从有效 EEG 坏导策略删除该源名称及已验证的电极别名。WPF / EDF 的 Raw / Montage 四条调用统一接收修正后的策略；不忽略其他未知坏导。新增测试覆盖大小写、辅助源不在 EEG 列中、CAR、exclude/interpolate，以及保留其他坏导处理。EDF/BDF 实际 API 测试携带重叠坏导配置完成 ICA 审查、预览、有效通道查询、频谱及全量/按需 PSD/RBP；别名 CAR 测试确认缓存仍命中。
- 坏段扫描通过共享 BuildPayload 取得有效参考配置，未确认 ICA 不移除参考源。切换去伪迹方法、参考参与的 ICA 确认/失效时清理旧报告。首次扫描自动启用 reject 若导致 ICA 失效并改变通道范围，丢弃这次旧范围响应并提示重新扫描，防止旧标注重新写回。
- 参考冲突仍发送配置失效事件以取消正在运行的分析和预览，但阻止自动重算；共享载荷入口拒绝冲突，避免非 ICA 方法丢弃 ECG 后悄悄执行。覆盖四种方法、手动执行拒绝、冲突期间重载、迟到预览、进行中分析取消及重选恢复。
- 通道徽标直接使用后端 ExcludedOutputChannels，不再依赖坏导策略为 exclude；none/interpolate/exclude 均覆盖。ECG 复用公共圆角控件样式，空值保留原协议含义，显示“自动识别（不指定）”；STA 测试验证已选区域、选项模板、重载绑定和原生渲染。
- 本轮后端 17 个相关模块：149 passed，0 failed（23.19 s）；另两项科学依赖边界检查：2 passed（0.26 s）。桌面相关范围：264 passed，0 failed（17 s）。后端 23 项重叠/参考聚焦测试也单独通过。测试失败修正后均已重跑对应完整相关范围，统计为最后通过结果。
- 文件规模、git diff --check 与严格 OpenSpec 校验通过；recording_access.py 为 428 行，保持现有 430 行登记上限并更新职责评估。当前任务共 8/8 完成，可按既有流程归档；本轮未提交/推送、未更改原有本地提交或 Git 暂存区。
- 验收边界维持：没有运行全量测试目录，也没有真实专用 EOG/ECG 硬件或生理识别准确率验收。没有改动滤波/ICA 科学公式或彩色阅图滤波。
