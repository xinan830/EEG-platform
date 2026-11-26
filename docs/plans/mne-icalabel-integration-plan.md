# `mne-icalabel` 离线 ICA 多类伪迹智能分类接入实施计划

> **文档用途**：作为下一阶段独立 OpenSpec 变更的完整工程与科学实施蓝图，供实现方按阶段编码落地，并作为最终逐条验收清单。  
> **遵守契约**：[工程开发契约](../工程开发契约.md) · [AGENTS.md](../../AGENTS.md)（400 行软上限、SI 单位 V/s/Hz、显式 NaN 缺口、不改原始记录）

---

## 1. 背景、目标与非目标

### 1.1 背景与痛点
当前离线科学预处理 `05 眼电 / 心电去伪迹`（[artifact_removal.py](../../backend/app/scientific/artifact_removal.py)）已具备完整的「整段连续拟合 → 2D 头皮热力图 + 10 秒极值小波形人工审查 → 指纹锁定 → 下游连续算法复用」闭环，但自动推荐仅依赖 `ica.find_bads_eog`（相关系数阈值）和 `ica.find_bads_ecg`：
- 难以自动识别**水平眼动（HEOG）**、**单通道电极跳变/局部接触噪声（Channel Noise）**、**持续肌电（EMG）**及无专用导联时的**心电（ECG）**；
- 审查弹窗卡片仅展示单一 `EOG r` / `ECG` 数值，新手难以快速判断某成分究竟是脑电还是哪类伪迹。

### 1.2 核心目标
1. 引入 MNE 官方生态的 **`mne-icalabel`**（基于 EEGLAB ICLabel 模型），使用轻量级 **`onnxruntime`（CPU）后端**，纯离线为每个 IC 成分输出 7 类概率标签：`brain`、`eye blink`、`muscle artifact`、`heart beat`、`line noise`、`channel noise`、`other`。
2. **定位为“AI 辅助打标 + 保守预勾选，人工最终拍板”**：不替代人工审查，不引入黑盒全自动删除；保留用户手动勾选、指纹锁定与模型复用机制。
3. **严格守住主分析链路 `1–50 Hz + 50 Hz 陷波` 契约不动**：复用与 `04 坏段质控` 相同的“宽带旁路副本（Fit on Copy, Apply to Target）”架构，仅在 ICA 拟合与 ICLabel 分类阶段构造临时宽带副本（`1–100 Hz`，受限于 Nyquist），拟合完毕后将空间解混矩阵应用回 `1–50 Hz` 主科学信号。
4. **严格准入校验 + 透明降级**：仅在满足 ICLabel 科学前提（CAR 平均参考 + 有效 3D 电极坐标 + Extended Infomax）时启用；不满足时**绝不强行推断或静默切换参考**，明确回退至现有 EOG/ECG 相关系数打分并在界面说明原因。

### 1.3 明确非目标（Out of Scope）
- **绝不引入 PyTorch（`torch`）** 或 GPU 推理依赖（避免体积暴涨 1.5–2.5 GB）。
- 不修改实时采集、普通彩色阅图滤波、STFT/TNK 有界分析（继续跳过 `05 去伪迹`）。
- 不修改主科学预处理及下游算法（PSD / RBP / FAA / IAPF / BrainBeat / 科学对比预览）的 `1–50 Hz + 50 Hz 陷波` 滤波定义。
- 不做“一键跳过审查直接全自动删成分”的黑盒模式。
- 不修改磁盘原始采集文件，不新增数据库迁移。

---

## 2. 科学架构设计

### 2.1 宽带拟合旁路（Fit on Copy, Apply to Target）

与现有 `04 坏段质控`（从未滤波 `source` 旁路提取 `110–140 Hz` 肌电区间，再掩膜回 `1–50 Hz` 主信号）保持完全一致的架构风格：

```text
坏导修复/排除 + 所选分析导联投影（未滤波 float64/V 宽带 source）
  ├──▶ [主科学链路] 1–50 Hz 零相位 + 50 Hz 陷波 (Q=30)
  │       └──▶ 04 坏段掩膜 (NaN) ──▶ 应用已确认 ICA 解混矩阵 ──▶ 下游 PSD / RBP / FAA / 科学对比预览
  │
  └──▶ [临时 ICA 拟合 & ICLabel 旁路副本]（仅启用 ica_infomax 且缓存未命中时创建，用完立即释放）
          ├── 1–high_fit_hz 零相位带通（统一公式 high_fit_hz = min(100.0, sfreq/2 - 2.0)，同步应用主链路选定的 notch_hz 陷波防止强工频挤爆 ICA 成分自由度）
          ├── 同步套用相同的 04 坏段 NaN 掩膜（防大爆幅带偏 ICA 权重）
          ├── 有效秩封顶：通过去均值协方差特征值计算 effective_rank，自动将成分数封顶为 min(requested, effective_rank)
          ├── Extended Infomax 拟合：ICA(method="infomax", fit_params={"extended": True}, max_iter=500, random_state=...)，显式记录收敛状态与样本充分性
          ├── 满足准入条件 → 挂载 3D DigMontage + 运行 mne_icalabel.iclabel.iclabel_label_components(..., inplace=False, backend="onnx")
          └── 产出 FittedIcaModel（解混矩阵 + 7 类概率 + 拟合诊断 + EOG/ECG 评分），写入 IcaReviewCache 并原子持久化 `.fif + .meta.json`
```

**关键科学约束**：
1. **必须使用 `Extended Infomax` + 有效秩与收敛守门**：`fit_params={"extended": True}, max_iter=500`；先计算 `effective_rank` 防止“插值 + CAR 降秩后仍强求过多成分”造成病态解混；若 `effective_rank < 3` 显式报错，若触及 `max_iter` 未收敛则降级为 `advisory_only`（关闭自动预勾选并提示人工谨慎复核），不再静默吞掉收敛警告。
2. **严禁跨参考混用解混矩阵**：
   - 若用户当前选的是 **平均参考（CAR）** 导联，宽带拟合副本与主科学链路均在同一个 CAR 空间下，解混矩阵数学严格一致，直接启用 ICLabel。
   - 若用户当前选的是 **原始硬件单参考（`REF`）** 或 **双极导联（如 `Fp1-F7`）**，**绝不允许**在后台偷偷转成 CAR 拟合再把 CAR 空间的 ICA 矩阵拿去解混 `REF` 数据！此时应照常在当前导联空间下拟合 Extended Infomax，但**跳过 ICLabel 分类**，回退至现有 EOG/ECG 相关系数推荐，并在响应与界面中给出明确中文说明。
3. **低采样率统一公式与受限态分离**：
   - 统一公式：`high_fit_hz = min(100.0, sfreq/2 - 2.0)`；
   - 当 `sfreq >= 204 Hz`（如 250 / 500 / 1000 Hz）：拟合副本使用完整 `1.0–100.0 Hz`，满足 ICLabel 训练分布，返回 `classifier_status="ready"`（蓝色就绪 + 保守自动预勾选）；
   - 当 `104 Hz <= sfreq < 204 Hz`（如 128 Hz，上限 62 Hz）：拟合副本降级至 `1.0–(sfreq/2 - 2.0) Hz`，允许输出 7 类概率供人工参考，但状态明确标为 **`classifier_status="ready_limited"`**（`classifier_unavailable_reason="fit_band_limited_by_nyquist"`），**关闭 ICLabel 自动预勾选**并在界面以暖琥珀提示高频肌电灵敏度受限；
   - 当 `sfreq < 104 Hz`：退回主滤波上限，跳过 ICLabel（`fallback_eog_ecg`, `sampling_rate_too_low_for_icalabel`）。
4. **保持既有通道物理身份、辅助参考通道与 NaN 缺口契约**：
   - 严格沿用 [recording_channel_roles.py](../../backend/app/services/recording_channel_roles.py) 与 [artifact_references.py](../../backend/app/services/artifact_references.py) 的“通道物理身份 vs 评分用途”分离机制：宽带拟合副本必须与主链路使用**完全相同的身份分区 EEG 会话**（`EEG` 脑电代理如 `Fp1`/`Fp2` 保留参与拟合与输出，已保存的专用 `EOG`/`ECG` 与 `unconfirmed` 源绝不进入宽带拟合或 3D Montage）；
   - **仅以已保存的物理身份区分专用 EOG 与 EEG 代理**（`has_dedicated_eog=bool(external_eog)`），绝不把 `explicit_channel` 当作专用物理 EOG；
   - 保留 `artifact_removal_validity` 的重构不可用区间溯源，绝不破坏已有缺口语义。

---

### 2.2 ICLabel 准入条件门控（Eligibility Gate）与降级规则

所有条件必须逐项显式判定并输出结构化状态，**禁止静默降级或吞掉收敛异常**：

| 检查项 | 通过条件 | 未通过时的 `classifier_status` | `classifier_unavailable_reason`（稳定枚举码） |
|---|---|---|---|
| 依赖检查 | `mne_icalabel`、`onnxruntime` 可导入 | `unavailable` | `icalabel_dependency_unavailable` |
| 模型文件 | 精确运行路径的 `ICLabelNet.onnx` 存在且可读取，哈希按文件状态更新 | `unavailable` | `icalabel_model_unavailable` |
| 推理运行时 | ONNX 推理成功；已知加载/执行故障显式返回不可用 | `unavailable` | `icalabel_inference_unavailable` |
| 参考方式 | 各输出参考池一致、无重复，且等于拟合电极全集 | `fallback_eog_ecg` | `non_average_reference_montage` |
| 样本参考一致性 | 拟合样本满足完整 CAR；分类前不再次减平均 | `fallback_eog_ecg` | `fit_samples_not_common_average` |
| 3D 电极坐标 | 全部参与 ICA 拟合的 EEG 通道均能解析到有效 3D 标准坐标且无重复位点（数量 ≥ 4） | `fallback_eog_ecg` | `electrode_3d_coordinates_incomplete` |
| 采样率（下限） | `sfreq >= 104.0 Hz` | `fallback_eog_ecg` | `sampling_rate_too_low_for_icalabel` |
| 采样率（完整频带） | `sfreq >= 204.0 Hz`（完整 `1–100 Hz` 训练分布） | `ready_limited`（仅参考，不自动预勾） | `fit_band_limited_by_nyquist` |
| 有效秩与收敛 | `effective_rank >= 3`（成分数自动封顶至 `effective_rank`）且 `ica_converged == True` | 秩不足报错；未收敛降级 `advisory_only`（不自动预勾） | `ica_not_converged` |
| 有效样本 | 去除坏段后连续有效样本 ≥ 5 秒（最低运行门槛，短于 `20 * k^2` 样本时标记 `short_recording` 诊断提示） | 抛出既有错误 | `ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES` |

- 全部通过时：`classifier_status = "ready"`，`classifier_method = "mne_icalabel_onnx"`。
- 任一前置条件不满足时：自动回退或降级为参考态，保证 `REF` / 双极 / 非标准通道 / 低采样率录音 100% 正常工作且 UI 透明告知原因。

---

### 2.3 7 类标签映射与保守推荐策略

`mne_icalabel.iclabel.iclabel_label_components(raw, ica, inplace=False, backend="onnx")` 输出固定 7 列顺序 `("brain", "muscle artifact", "eye blink", "heart beat", "line noise", "channel noise", "other")` 与前端中文展示映射：

| 英文标签 (`iclabel_label`) | 中文显示名 | 类别属性 | 是否参与自动预勾选 |
|---|---|---|---|
| `brain` | 大脑脑电 | 有效信号 | 否（绝对保护，永不自动预勾） |
| `eye blink` | 眼电伪迹 | 伪迹 | 是 |
| `muscle artifact` | 肌电伪迹 | 伪迹 | 是 |
| `heart beat` | 心电伪迹 | 伪迹 | 是 |
| `line noise` | 工频噪声 | 伪迹 | 是 |
| `channel noise` | 单导噪声 | 伪迹 | 是 |
| `other` | 其他成分 | 不确定 | 否（绝对保护，交由人工复核） |

**保守预勾选与脑电保护伞规则（防止误删真实脑电）**：
- `0.75 / 0.25` 为工程初始保守阈值（非临床正确率承诺），明确记录规则版本 `iclabel-rec-v2-brain-guard`；
- 仅当 `classifier_status == "ready"` 且 `ica_converged == True` 时，满足以下**全部条件**的成分标记 `recommended_exclude = True`：
  1. `iclabel_label in {"eye blink", "muscle artifact", "heart beat", "line noise", "channel noise"}`；
  2. `iclabel_probability >= 0.75`；
  3. `iclabel_probabilities["brain"] < 0.25`；
- **脑电/其他绝对保护伞优先**：即使配置了经物理身份确认的**专用物理 EOG / ECG 通道**（`has_dedicated_eog=bool(external_eog)` 或存在 `ecg_channels_used`），只要 ICLabel 将该成分判为 `brain`/`other` 或 `brain >= 0.25`，**绝不自动预勾选**，仅将相关性冲突记入 `advisory_conflicts` 在 Tooltip 提示人工复核；仅对非保护类成分允许并入推荐并在 `recommendation_reasons` 完整溯源。

---

### 2.4 指纹、可审计版本证据与模型持久化/过期防漂移契约

- [ica_review_cache.py](../../backend/app/services/ica_review_cache.py)：
  - 将 `compute_ica_fit_fingerprint` 升级为 **`"ica-fit-v6-notched-broadband-fit"`**，由审查与执行共用单一入口 `compute_recording_ica_fit_fingerprint`；
  - 纳入 `ica_extended: True`、`ica_max_iter`、`fit_low_hz`、`fit_high_hz`、`classifier_version`、`onnxruntime_version`、`iclabel_model_sha256`（`ICLabelNet.onnx` 权重 SHA-256）、`recommendation_rule_version`；
  - 内存 LRU（容量 16）+ 磁盘原子持久化（MNE 公开 `ica.save` / `read_ica` 生成 `<fingerprint>-ica.fif` + `.meta.json` + `.fif` SHA-256 校验，零 `pickle`，随 `invalidate_recording` 同步清理）；
  - **严格过期守门（消除静默重拟合编号漂移）**：当执行请求携带已确认的 `ica_fit_fingerprint`，若内存与磁盘均无法恢复该已审查模型，**严禁静默重拟合盲套旧成分编号**，必须显式抛出 `ICA_REVIEW_MODEL_EXPIRED` 引导重新审查。
- [contracts/artifact_removal.py](../../backend/app/scientific/contracts/artifact_removal.py)：
  - 将 `ARTIFACT_REMOVAL_APPLICATION_VERSION` 升级为 **`"artifact-removal-application-v8-notched-broadband-ica"`**。

---

## 3. 依赖管理、体积与合规要求

1. **仅新增轻量依赖**（[backend/pyproject.toml](../../backend/pyproject.toml)）：
   - `mne-icalabel>=0.7`
   - `onnxruntime>=1.18`（CPU 版，约 25–40 MB）
   - **硬性禁令**：安装后执行 `pip list` 验收，环境中**绝对不得出现 `torch`、`torchvision`、`torchaudio`**。
2. **纯离线运行保障**：
   - 验证 `mne-icalabel` 的 `iclabel` ONNX 模型文件（`.onnx`）随 Python 包内置安装，在**断网环境**（或阻断外网访问的单元测试）下可直接加载推理，不触发首次运行联网下载。
   - 若 `mne-icalabel` 依赖 `pooch` 且存在缓存读取逻辑，须验证离线无网络异常。
3. **开源协议与引用登记**：
   - 在 `backend/app/assets/` 或文档中补充 `mne-icalabel`（BSD-3-Clause）、`onnxruntime`（MIT）及 ICLabel 论文引用（Pion-Tonachini et al., *NeuroImage* 2019）说明。
4. **单次推理耗时目标**：
   - CPU 环境下对 15–20 个 IC 成分运行 `onnxruntime` 分类耗时应控制在 **< 1.0 秒**，不得显著拖慢 ICA 审查弹窗打开体验。

---

## 4. API 与桌面端契约变更

### 4.1 后端接口扩展（向后兼容新增字段）

接口路径不变：`GET /api/recordings/{recording_id}/preprocessing/ica-components`

**顶层新增字段**：
```json
{
  "ica_algorithm": "extended_infomax",
  "fit_bandpass_hz": [1.0, 100.0],
  "fit_band_limited_by_nyquist": false,
  "classifier_method": "mne_icalabel_onnx",
  "classifier_status": "ready",
  "classifier_unavailable_reason": null,
  "classifier_version": "mne-icalabel-0.x-onnx",
  "iclabel_exclude_threshold": 0.75,
  "iclabel_max_brain_probability": 0.25
}
```
（当回退时：`classifier_method: "eog_ecg_correlation"`, `classifier_status: "fallback_eog_ecg"`, `classifier_unavailable_reason: "non_average_reference_montage"` 等。）

**`components[]` 每项新增字段**：
```json
{
  "component_index": 3,
  "eog_score": 0.91,
  "ecg_score": null,
  "explained_variance_ratio": 0.24,
  "iclabel_label": "eye blink",
  "iclabel_probability": 0.9621,
  "iclabel_probabilities": {
    "brain": 0.0112,
    "muscle artifact": 0.0085,
    "eye blink": 0.9621,
    "heart beat": 0.0021,
    "line noise": 0.0004,
    "channel noise": 0.0042,
    "other": 0.0115
  },
  "recommendation_reasons": ["iclabel:eye_blink:0.96"],
  "recommended_exclude": true,
  "excluded": true
}
```
（当回退时：`iclabel_label: null`, `iclabel_probability: null`, `iclabel_probabilities: null`，原有 `eog_score` / `ecg_score` 照常填充。）

### 4.2 桌面端 UI 改造规范（严格沿用 3 色克制体系）

延续已确立的 **3 色规范（中性灰白 + 品牌蓝 `#2563EB` + 暖琥珀 `#D97706`）**，严禁引入花花绿绿的多色徽章：

1. **DTO 与 ViewModel**（[PreprocessingContracts.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Algorithms/Contracts/PreprocessingContracts.cs)、[ReviewPreprocessingModels.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Preprocessing/ReviewPreprocessingModels.cs)）：
   - `IcaInspectionResponse` 映射顶层 `classifier_status`、`classifier_unavailable_reason`、`fit_bandpass_hz` 等字段；
   - `IcaComponentItem` 与 `IcaComponentCardViewModel` 映射 `IclabelLabel`、`IclabelProbability`、`IclabelProbabilities`、`RecommendationReasons`；
   - 卡片 `ScoreBadgeText` 优先展示 ICLabel 中文主标签与百分比（如 `眼电 96%`、`单导噪声 89%`、`肌电 82%`、`大脑 91%`、`其他 64%`），鼠标悬停 Tooltip 展示完整 7 类概率分布 + 原 EOG/ECG 相关系数；回退态下保持现有 `EOG r=...` / `ECG ...` 展示。
2. **ICA 审查弹窗**（[IcaReviewDialog.xaml](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Views/IcaReviewDialog.xaml) & `.xaml.cs`）：
   - 顶部副标题明确展示当前分类器状态：
     - `ready`：`ICLabel 智能分类已就绪（1–100 Hz 宽带拟合，Extended Infomax）· 已按高置信度预勾选疑似伪迹，请复核确认`；
     - `fallback_eog_ecg`（例如 REF 参考）：温和提示 `当前为非平均参考导联（REF），ICLabel 需切换至平均参考 (CAR) 启用；当前已自动使用 EOG/ECG 相关评分`。
   - 卡片顶部增加轻量的类别筛选/快捷操作（如 `全部 / 仅看建议剔除` 或 `恢复系统推荐`），卡片视觉仍仅用中性灰（大脑/未勾选）与暖琥珀高亮（待剔除/建议剔除）。
3. **步骤弹窗与科学对比缓冲衔接**（[ArtifactRemovalStepDialog.xaml](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Views/Preprocessing/ArtifactRemovalStepDialog.xaml)）：
   - 仅在 `IcaActionCard` 内更新说明文案：明确 `Infomax ICA` 已升级为 `Extended Infomax`，在平均参考（CAR）下自动启用 ICLabel 7 类伪迹识别（眼电/肌电/心电/单导噪声/工频），主分析频段仍保持 `1–50 Hz`；**不得破坏**既有的按去伪迹方式动态显隐（`IcaActionCard` / `SspInfoCard` / `EogRegressionInfoCard`）与独立模态弹窗 [ChannelIdentityDialog.xaml](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Views/Preprocessing/ChannelIdentityDialog.xaml)。
   - 确认 ICA 剔除选择后通过 `NotifyConfigAndVisuals()` 自动清空 [ScientificComparisonTileCache.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Preprocessing/ScientificComparisonTileCache.cs) 并无缝刷新科学对比预览（滚动与播放均保持零闪烁）。

---

## 5. 模块拆分与文件规模约束（遵守 400 行目标软上限与职责评审机制）

> **说明**：项目以 **400 行为业务文件目标软上限**（超出须按职责评审并在 `docs/code-size-policy.json` 登记上限）。`backend/app/scientific/artifact_removal.py` 已登记上限为 460 行，本次将 `FittedIcaModel`/`ArtifactRemovalResult` 归入 `contracts/artifact_removal.py` 并将 ICLabel 独立拆分至 `ica_classifier.py`，使 `artifact_removal.py` 保持在已登记上限内；`ica_classifier.py` 的科学职责评审与上限见 `docs/code-size-policy.json`。

| 层级 | 文件路径 | 变更类型 | 职责与行数目标 |
|---|---|---|---|
| 后端依赖 | `backend/pyproject.toml` | 修改 | 新增 `mne-icalabel`、`onnxruntime`（无 `torch`） |
| 后端科学 | `backend/app/scientific/ica_classifier.py` | **新增** | 封装准入条件门控、3D Montage 绑定、ONNX 后端 `mne-icalabel` 推理、7 类概率格式化、保守推荐判定与优雅降级（按当前规模评审登记上限） |
| 后端科学 | `backend/app/scientific/artifact_removal.py` | 修改 | 升级为 `fit_params={"extended": True}`，支持接收可选宽带拟合输入与 `signal_provenance`，调用 `ica_classifier.py` 合并打分与推荐，**严格控制在 ≤ 460 行（目标缩减）** |
| 后端契约 | `backend/app/scientific/contracts/artifact_removal.py` | 修改 | 升级 `ARTIFACT_REMOVAL_APPLICATION_VERSION` 至 `artifact-removal-application-v8-notched-broadband-ica` |
| 后端服务 | `backend/app/services/ica_review_cache.py` | 修改 | 升级指纹版本至 `ica-fit-v6-notched-broadband-fit` 并纳入宽带拟合与分类器版本字段 |
| 后端服务 | `backend/app/services/artifact_removal_preparation.py` & `artifact_references.py` | 修改 | 统一审查（`prepare_reviewed_ica`）与执行（`apply_prepared_removal`）的宽带拟合副本构造与 `signal_provenance` 传递，**两条路径复用同一审查指纹与模型；持久化恢复按数值容差验证输出一致性** |
| 后端服务 | `backend/app/services/mapped_preprocessing.py` & `artifact_inspection.py` | 修改 | 仅在启用 `ica_infomax` 且缓存未命中时准备临时宽带副本（`1–min(100, sfreq/2-1) Hz` + 同坏段掩膜）并在拟合后立即释放；返回新增分类器与 7 类概率字段 |
| 桌面契约 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/Contracts/PreprocessingContracts.cs` | 修改 | 扩展 `IcaInspectionResponse` 与 `IcaComponentItem` 新增字段 |
| 桌面模型 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Preprocessing/ReviewPreprocessingModels.cs` | 修改 | 卡片中文标签、概率百分比、7 类分布 Tooltip 与推荐原因（保留与 EOG/ECG 相关分互补展示） |
| 桌面视图 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Views/IcaReviewDialog.xaml` & `.xaml.cs` | 修改 | 展示分类器状态/回退提示、7 类中文标签与筛选交互（≤ 200 行） |
| 桌面视图 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Views/Preprocessing/ArtifactRemovalStepDialog.xaml` | 修改 | 仅在 `IcaActionCard` 同步 Extended Infomax + ICLabel 说明（保持方式感知与 `ChannelIdentityDialog` 入口不变） |

---

## 6. 分阶段实施任务清单

### 阶段 1：依赖接入与离线能力验证
- [x] **1.1** 更新 `backend/pyproject.toml`，在 `.venv` 中安装 `mne-icalabel` 与 `onnxruntime`（确认未引入 `torch`）。
- [x] **1.2** 编写离线冒烟测试，验证 `mne_icalabel.iclabel.iclabel_label_components(..., inplace=False, backend="onnx")` 在无网络访问下可正常输出完整 7 类概率矩阵，实测 18 成分 CPU 推理耗时 < 1 秒（0.43 s）。

### 阶段 2：后端 `ica_classifier.py` 与准入/降级契约（TDD）
- [x] **2.1** 新增 `backend/tests/test_ica_classifier.py`，覆盖 CAR 就绪、REF/双极/缺 3D 坐标/低采样率回退、128 Hz `ready_limited`、未收敛 `advisory_only`、有效秩计算、脑电/其他绝对保护伞与可审计权重 SHA-256。
- [x] **2.2** 实现 `backend/app/scientific/ica_classifier.py` 并通过全部单测。

### 阶段 3：宽带拟合旁路、Extended Infomax、有效秩与持久化防漂移
- [x] **3.1** 在 `artifact_removal.py` 升级为 `fit_params={"extended": True}, max_iter=500`，实现有效秩自动封顶、显式收敛/样本充分性诊断，并支持宽带拟合副本仅将解混矩阵应用回 `1–50 Hz` 主信号。
- [x] **3.2** 更新 `artifact_removal_preparation.py`、`artifact_references.py`、`mapped_preprocessing.py` 与 `artifact_inspection.py`：仅在启用 `ica_infomax` 且缓存未命中时构建临时 `1–min(100, sfreq/2-2) Hz` 宽带副本，同步施加相同坏段 `NaN` 掩膜，拟合完毕立即释放临时 mmap。
- [x] **3.3** 升级 `ica_review_cache.py` 指纹版本（`ica-fit-v6-notched-broadband-fit`）与 `ARTIFACT_REMOVAL_APPLICATION_VERSION`（`artifact-removal-application-v8-notched-broadband-ica`），实现 `.fif + .meta.json` 原子持久化与 `ICA_REVIEW_MODEL_EXPIRED` 过期防漂移守门。
- [x] **3.4** 新增后端集成回归测试（`backend/tests/test_ica_label_integration.py`），覆盖多伪迹 1–50 Hz 无泄漏、插值+CAR 秩封顶、含传感器噪声夹具的实际推荐剔除与 α/β 保留，以及三组无伪迹空间源的空剔除列表、全信号误差与 α/β 功率保留（不等同于真实 EEG 误删率验证）、跨重启持久化恢复与过期拒收。

### 阶段 4：桌面端契约、审查弹窗与回归测试
- [x] **4.1** 同步 `PreprocessingContracts.cs` 与 `ReviewPreprocessingModels.cs`，保留旧字段向后兼容，渲染中文 7 类标签、7 类概率分布 Tooltip 与脑电保护冲突提示（`advisory_conflicts`）。
- [x] **4.2** 更新 `IcaReviewDialog.xaml` / `.xaml.cs` 与 `ArtifactRemovalStepDialog.xaml`：保持严格 3 色克制风格，区分 `ready` / `ready_limited` / `advisory_only` / `fallback_eog_ecg` 状态与有效秩/收敛诊断提示，支持「全部 / 仅看建议剔除」筛选与「恢复系统推荐」。
- [x] **4.3** 新增 WPF 单元测试（`ReviewIcaLabelDialogTests.cs`），覆盖 7 类标签、Tooltip、脑电冲突提示、`ready_limited`/`advisory_only`/秩封顶横幅、`fallback_eog_ecg` 回退、筛选与推荐重置及 `IcSparklineControl` 零回归。

### 阶段 5：全量回归与真实录音验收
- [x] **5.1** 本次涉及的前后端预处理/通道身份/预览/Review 回归套件全部通过（零新增失败；仓库既有无关历史基线问题另行记录）；桌面端 `BrainPlatform.Desktop` Release 构建 0 警告 0 错误。
- [ ] **5.2** 使用同一份真实录音在桌面端分别切换 **平均参考（CAR）** 与 **硬件参考（REF）** 实机复核：
  - 依据成分的**空间头皮拓扑、时域波形、频谱与独立参考证据**人工复核分类合理性（不依赖固定成分编号，也不将 `0.75` 概率阈值等同于临床准确率），并确认干净脑电成分不被过度预勾选；
  - 确认审查后在科学对比、下游 PSD/RBP 与后端重启场景下均复用同一持久化审查模型；REF 下明确提示切 CAR 并回退 EOG/ECG。
- [x] **5.3** 执行文件规模检查、`uv pip list` 确认无 `torch`、`git diff --check` 全部通过。

---

## 7. 最终验收标准（Acceptance Checklist）

1. **零重型依赖**：`backend/.venv` 与 `pyproject.toml` 中绝对不含 `torch`；仅使用 `mne-icalabel` + `onnxruntime` CPU 推理；断网离线可运行。
2. **主链路零污染**：下游 PSD / RBP / FAA / 科学对比预览的输出信号仍严格维持 `1–50 Hz + 50 Hz 陷波`，不受 `1–100 Hz` 宽带拟合副本影响。
3. **严禁跨参考混算与伪就绪**：非 CAR（`REF` / 双极）录音绝不偷偷重参考拟合；插值/CAR 降秩时按有效秩自动封顶成分数；`104–204 Hz` 受限输入标记 `ready_limited`、未收敛标记 `advisory_only`，均关闭自动预勾选并显式提示。
4. **脑电保护与人工拍板**：`brain` 与 `other` 成分绝不自动预勾选（含专用 EOG/ECG 冲突场景）；最终剔除成分必须经人工审查弹窗确认并受 `ica_fit_fingerprint` 绑定。
5. **模型持久化、防漂移与缺口零回归**：已审查模型优先由内存/磁盘持久化按指纹无漂移复用，失效或缺失时显式返回 `ICA_REVIEW_MODEL_EXPIRED` 要求重新审查（绝不静默重拟合盲套旧编号）；坏段 `NaN` 掩膜与 `artifact_removal_validity` 保持完整。
6. **UI 审美与规模规范**：保持严格 3 色风格（中性灰白 + 品牌蓝 + 暖琥珀）；遵守 400 行目标软上限与职责评审登记机制；`BrainPlatform.Desktop` Release 0 警告 0 错误；本次涉及的前后端新增与回归测试全部通过（零新增警告/零新增失败）。

## 8. 科学正确性修复（2026-11-26）

- ICLabel 只接受拟合 EEG 全集构成的完整 CAR，分类副本只标记参考元数据，不再次减平均；部分参考池或样本不满足 CAR 时显式回退。
- 收敛按 MNE 未舍入的权重变化和连续小角度停止条件判断；缺失证据时禁用自动推荐。
- ICA 无需 EOG/ECG 即可拟合和分类；无参考时不提供相关分，不静默启用额区代理。
- 模型文件缺失或已知 ONNX 运行故障返回结构化 unavailable；程序错误与非法科学输出仍报错。
- `uv.lock` 与项目依赖同步；FIF 恢复允许伪逆重算产生的浮点舍入，完整输出使用 rtol=1e-12、atol=1e-15 V 验证，不承诺逐位相同。
- 当前变更契约与测试证据：`openspec/changes/fix-iclabel-scientific-validation/`。真实 EEG 的分类准确率和桌面人工验收尚不由这些合成回归证明。

### 8.1 复核补齐

- 模型哈希只缓存成功读取，键含精确路径、文件大小及纳秒级修改/变更时间；缺失、恢复、替换无需重启即可刷新状态。
- `test_ica_clean_signal_preservation.py` 用固定三组空间源实际运行 ICLabel 与推荐应用，检查实际剔除列表为空、α/β 功率保留 95%–105%、全信号相对误差 < 1e-12；强制删除 α 成分的对照必须检出功率降至 50% 以下。这些是合成回归阈值，不是临床准确率。
- 原秩封顶夹具包含独立白色传感器噪声，不能称为纯干净脑电：现在实际应用推荐集合并检查 α/β 幅度保留 90%–110%，记录被剔除集合与全信号变化，不把传感器噪声剔除率当作脑电误删率。
