# 官方算法动态与稳健性验证（阶段 4）

日期：2026-10-20<br>
范围：合成/夹具记录的故障注入、动态窗口和结果持久化鲁棒性。此报告是工程验证，不是临床验证。

## 故障注入

新增 backend/tests/test_algorithm_robustness_audit.py，注入：

- 少一个样本：质量原因 missing_samples；Welch 窗口不可用，返回空 PSD，而非零频谱。
- 单点 250 µV 尖峰：amplitude_threshold。
- 连续重复极值：clipping。
- 全 NaN：non_finite，gap evidence 记录非有限样本数。
- 少于 4 秒的 Welch 输入：low_quality，返回 shape (1, 0) 的空结果。

已有 test_spectral_quality.py 和 test_official_algorithm_extended_audit.py 继续核验平线、坏 PSD、坏 STFT 帧输出 NaN、相邻有效帧继续计算，以及不可用输出不填零。动态 WPF/API fixture 的 gap 和高幅值拒绝场景还逐点对照 Full Run 与按需窗口：拒绝/不可用状态、failure code、实际样本范围和证据必须匹配；后续窗口仍能成功，原始记录哈希和 Full Run artifact 不变。

## 动态时间轴与质量状态

核验来源包括 test_official_algorithm_runs.py、test_result_window_index.py、test_on_demand_psd_api.py、test_on_demand_rbp_api.py、test_on_demand_scalar_api.py 和 test_official_algorithm_extended_audit.py：

- endpoint 锚定：time_s == window_end_s；cursor 到样本坐标向下取整，不选择未来窗。
- 请求/实际范围保留 start_sample、end_sample 左闭右开坐标；交叠窗口使用已声明 step，缺口返回 gap/无窗口，不沿用旧结果。
- warm-up 标记 Partial，达到完整动态窗后为 Complete；质量 rejected/unavailable 窗口保留独立状态和 null，不被下一窗的数值覆盖。
- 动态按需结果与全量结果逐窗比对数组、频率轴、单位、quality、failure 和 evidence。

## 取消、部分失败、缓存与历史 artifact

已执行的回归覆盖：

- full Run 取消与 on-demand 窗口执行隔离；窗口订阅取消、共享订阅继续、显式 retry generation。
- 写 artifact 后 metadata 发布被注入中断：窗口失败、result 为 null、未登记 artifact，孤儿文件清理。
- 已 ready 窗口的 metadata/payload 缺失或损坏返回结构化 409，不重新计算、不改写已保存结果。
- stale lease、重复发布不能覆盖 ready 结果；并发 retry 只创建一个后继。
- canonical artifact、窗口 chunk、axis 和 index 的 SHA-256/identity/fingerprint 损坏或不匹配均拒绝读出；Full PSD 遇到身份不符的按需缓存记录 cache miss 并重新计算。
- 历史 Run/artifact 的缺失、截断、过期格式和错误 row index 显式失败；immutable canonical artifact 保持原字节。

## 实时 BrainBeat 的边界

本阶段仅确认离线 brainbeat 是逐窗无状态算法。旧实时 BrainBeat 的 IAPF lock、候选峰状态、EMA warm-up 与缺样本/重置行为不由离线测试背书。实时链路处于 legacy/shadow 路径，后续若评估，需单独冻结状态转移、窗口序列、lock/解锁策略、EMA 参数和重置语义，并测试输入顺序、重复/丢失窗口、长间断恢复及状态 provenance。

## 验证结果

- 故障与质量/边界组：56 passed，保留 5 条已知退化输入 warning（Peak Frequency 空切片和 IAPF 非正/非有限 log 运算）。
- 动态逐窗组：50 passed。
- 取消、artifact 故障和按需生命周期组：52 passed。
- 总计以上三组运行分别通过；组合计数含跨组重复用例，不能解释为唯一用例总数。

没有执行 GUI 人工验收。夹具是合成的非身份 EEG；此阶段未使用真实记录，也不支持临床效度结论。
