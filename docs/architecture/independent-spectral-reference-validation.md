# 独立频谱参考校验

`offline-spectral-v3` 的生产 PSD 可以通过独立实现进行工程校验。该能力回答的是“两个明确实现是否在明确容差内数值一致”，不构成临床有效性或诊断结论。

## 使用方式

向下列端点提交要校验的完整时间区间和通道顺序：

```text
POST /api/recordings/{recording_id}/validations/spectral-reference
```

```json
{
  "start_s": 10.0,
  "end_s": 40.0,
  "channels": ["F3", "Fz", "Pz"]
}
```

成功时返回 `201` 和一个 `ValidationRun`。其 `evidence` 包含频率坐标、独立参考 PSD、生产 PSD、通道顺序、单位和质量摘要。频率值、PSD 值均是后端返回的派生算法结果；原始 EEG 样本和源文件名不会被写入校验证据。

质量门无法产出有限 PSD 时，端点返回 `422` 和稳定错误码 `SPECTRAL_REFERENCE_UNAVAILABLE`，且不会用零值创建校验记录。

## 独立计算边界

参考实现位于 `backend/app/services/independent_spectral_reference.py`。它不调用生产的 `preprocess_offline`、`estimate_welch_psd` 或 `band_power`，而是使用 NumPy/SciPy 独立完成以下固定契约：

1. MNE 读取存储的 BDF/EDF；
2. 整段 1--30 Hz、四阶 Butterworth SOS `sosfiltfilt`；
3. 用采样索引截取实际分析区间；
4. 4 s Hann segment、2 s 步进、每段 constant detrend Welch；
5. 仅平均独立质量规则判定为 clean 的 segment；
6. 取 1--30 Hz，边界处一次性由 V2/Hz 转换为 uV2/Hz。

默认容差为 `rtol=1e-7` 和 `atol=1e-9 uV2/Hz`。比较按“请求通道顺序，再按升序频率”扁平化；因此可复核每一个频率点。

## 追溯与导出

`ValidationRun` 保存源文件 SHA-256、文件大小、配置摘要、运行环境、容差、最大误差、逐点通过率与派生证据。通过：

```text
GET /api/validations/{validation_id}/report
GET /api/runs/{run_id}/export?validation_id={validation_id}
```

可获得工程校验报告或包含 `validation-report.json` 的结果导出包。导出包的校验报告同样明确标记为 engineering-only，不应被解释为临床结论。
