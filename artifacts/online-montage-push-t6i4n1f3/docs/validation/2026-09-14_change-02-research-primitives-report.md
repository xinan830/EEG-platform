# Change 02 科研积木验证报告

## 结论

`standardize-research-primitives` 在 2026-09-14 完成工程验证。该变更新增了后端限定科研积木，未替换任何已有 API、Viewer Pipeline 或 `offline-spectral-v3` 的正式结果路径。它证明类型、单位、时间、质量和 provenance 可以在未来算法执行器之前被一致检查；不证明任何指标具有临床有效性。

## 范围

- 值对象：`EEGSignal`、`WindowedSignal`、`PSDSeries`、`BandPower`、`RelativePower`、`Scalar`、`TimeSeries`、`ChannelMap`、`QualityMask`。
- 单位：`V`、`uV`、`V^2`、`uV^2`、`V^2/Hz`、`uV^2/Hz`、`Hz`、`s`、ratio、percent、dimensionless、`dB re 1 uV^2/Hz`。
- 安全节点：选通道、重参考、滤波、陷波、polyphase 重采样、去趋势、窗口、Welch、频段积分、RBP、质量门、算术和统计。
- 参考组合：固定频段 RBP 与 `ln(alpha F4) - ln(alpha F3)` FAA。

没有新增数据库、HTTP API、前端科学计算、DAG 持久化、用户公式、动态导入或任意 Python 执行。

## Evidence -> Finding -> Path

### 不可变性与通道顺序

**Evidence:** `test_research_primitives.py` 尝试写入值对象数组时得到只读错误；请求 `Oz,F3` 后结果仍以 `Oz,F3` 顺序返回。

**Finding:** 节点不能就地污染上游数组，通道顺序不是隐式字典顺序。

**Path:** `types.py` 的 `_readonly()`、`ChannelMap.indices()` -> `signal_nodes.select_channels()`。

### 单位与安全执行边界

**Evidence:** 测试证明 `V^2 + uV^2` 和 `V^2 + Hz` 都在产生输出前失败；未知节点名被 `UnknownNodeError` 拒绝。

**Finding:** 不存在静默单位缩放、`eval`、动态 import 或用户 lambda 的执行通道。

**Path:** `units.py` -> `math_nodes.py`；`registry.py` 的闭合 `NODE_REGISTRY`。

### 时间、重采样与质量

**Evidence:** 10 秒、4 秒窗口、2 秒步长产生绝对中心 `2,4,6,8 s`；残窗可明确 drop 或 reject。200 Hz 的 70 Hz 正弦重采样到 100 Hz 后，带外能量受 polyphase FIR 抑制。坏窗口即使不导致总门失败，也保留 `rejected_reasons`；总门失败时 PSD 与 Band Power 为 `None`。

**Finding:** 时间轴、抗混叠方式和质量失败都不会被界面或零值掩盖。

**Path:** `signal_nodes.window()` / `resample()`；`spectral_nodes.welch_psd()`。

### `offline-spectral-v3` 数值回归

**Evidence:** 对同一 30 秒合成、V/float64 信号，primitive 4 秒 Hann / 2 秒步进 Welch 的频率轴和全部 PSD 点与既有 `estimate_welch_psd()` 在 `rtol=1e-12, atol=1e-24` 下逐点一致。

**Finding:** 新积木可以影子复现冻结的 v3 PSD，包括其 `1e-20 V^2/Hz` 下限；当前官方结果没有切换。

**Path:** `window()` -> `welch_psd()` compared with `eeg_core.spectral.estimate_welch_psd()`。

### RBP 与 FAA

**Evidence:** 四个半开/末端闭合频段的 RBP 合计为 1；F4 振幅为 F3 两倍的合成 alpha 信号得到 FAA 接近 `ln(4)`。由于 v3 的密度下限，该理论值允许 `1e-8` 绝对容差。

**Finding:** 组合只使用命名通道、明确积分和对数节点；没有在前端或未受控表达式中重算。

**Path:** `compositions.fixed_band_rbp()`、`compositions.frontal_alpha_asymmetry()`。

## 执行命令和结果

```powershell
openspec validate --all --strict --no-interactive
# 10 passed, 0 failed

cd backend
.\.venv\Scripts\python.exe -m pytest -q
# 125 passed, 2 third-party deprecation warnings

cd ..\frontend
npx vue-tsc --noEmit
npm test -- --run
npm run build
# all passed (exit code 0); no frontend source changed

cd ..
git diff --check
# passed
```

## 文件规模和残余风险

所有新增业务文件低于 400 行，故不需要更新 `docs/code-size-policy.json`。

- 这是一套受限 API，不是通用维度代数；未来增加物理运算必须先扩展单位契约和测试。
- `resample_poly` 的实际有理采样率会写入 provenance，不能假定任意浮点请求都能无误差表示。
- primitive Welch 与 v3 的一致性是生产一致性验证，不能取代已有独立 SciPy/MNE reference 验证。
- 图定义、拓扑排序、发布版本、持久化和正式算法切换严格留给 Change 03 和 Change 04。
- 合成数组用于本次证据；没有读取或提交真实 EEG、SQLite、NPZ artifact 或个人身份信息。
