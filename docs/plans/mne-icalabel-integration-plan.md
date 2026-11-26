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
          ├── 1–high_fit_hz 零相位带通（high_fit_hz = min(100.0, sfreq/2 - 1.0)，保留 50–100 Hz 肌电/工频特征）
          ├── 同步套用相同的 04 坏段 NaN 掩膜（防大爆幅带偏 ICA 权重）
          ├── Extended Infomax 拟合：ICA(method="infomax", fit_params={"extended": True}, random_state=...)
          ├── 满足准入条件 → 挂载 3D Montage + 运行 mne_icalabel.label_components(..., method="iclabel")
          └── 产出 FittedIcaModel（解混矩阵 + 7 类概率 + EOG/ECG 评分），写入 IcaReviewCache
```

**关键科学约束**：
1. **必须使用 `Extended Infomax`**：`fit_params={"extended": True}`，满足 ICLabel 训练分布要求（同时准确分离亚高斯工频/心电与超高斯眨眼/单导跳变）。
2. **严禁跨参考混用解混矩阵**：
   - 若用户当前选的是 **平均参考（CAR）** 导联，宽带拟合副本与主科学链路均在同一个 CAR 空间下，解混矩阵数学严格一致，直接启用 ICLabel。
   - 若用户当前选的是 **原始硬件单参考（`REF`）** 或 **双极导联（如 `Fp1-F7`）**，**绝不允许**在后台偷偷转成 CAR 拟合再把 CAR 空间的 ICA 矩阵拿去解混 `REF` 数据！此时应照常在当前导联空间下拟合 Extended Infomax，但**跳过 ICLabel 分类**，回退至现有 EOG/ECG 相关系数推荐，并在响应与界面中给出明确中文说明。
3. **低采样率自适应**：
   - 当 `sfreq >= 204 Hz`（如 250 / 500 / 1000 Hz）：拟合副本使用 `1.0–100.0 Hz`（完整保留 50–100 Hz 肌电/工频特征）；
   - 当 `104 Hz <= sfreq < 204 Hz`：拟合副本自动降级至 `1.0–(sfreq/2 - 2.0) Hz`，允许运行 ICLabel 但在证据中标注 `fit_band_limited_by_nyquist=True`（提示高频肌电灵敏度受采样率限制）；
   - 当 `sfreq < 104 Hz`：退回主滤波上限，跳过 ICLabel 并在原因中注明采样率不足。
4. **保持既有辅助参考通道与 NaN 缺口契约**：
   - 保留 [artifact_references.py](../../backend/app/services/artifact_references.py) 的独立 EOG/ECG 参考发现与隔离逻辑；
   - 保留 `artifact_removal_validity` 的重构不可用区间溯源，绝不破坏已有缺口语义。

---

### 2.2 ICLabel 准入条件门控（Eligibility Gate）与降级规则

所有条件必须逐项显式判定并输出结构化状态，**禁止静默降级或捕获全部异常吞错**：

| 检查项 | 通过条件 | 未通过时的 `classifier_status` | `classifier_unavailable_reason`（稳定枚举码） |
|---|---|---|---|
| 依赖检查 | `mne_icalabel` 与 `onnxruntime` 可导入且离线权重就绪 | `unavailable` | `icalabel_dependency_unavailable` |
| 参考方式 | 当前导联所有输出均属于同一平均参考（`negative_kind == "mean"` / CAR）且非双极 | `fallback_eog_ecg` | `non_average_reference_montage` |
| 3D 电极坐标 | 全部参与 ICA 拟合的 EEG 通道均能解析到有效 3D 标准坐标且无重复位点（数量 ≥ 4） | `fallback_eog_ecg` | `electrode_3d_coordinates_incomplete` |
| 采样率 | `sfreq >= 104.0 Hz`（足够覆盖基础高频特征） | `fallback_eog_ecg` | `sampling_rate_too_low_for_icalabel` |
| 有效样本 | 去除坏段后连续有效样本 ≥ 5 秒（与现有 ICA 门槛一致） | 抛出既有错误 | `ARTIFACT_REMOVAL_INSUFFICIENT_FINITE_SAMPLES` |

- 全部通过时：`classifier_status = "ready"`，`classifier_method = "mne_icalabel_onnx"`。
- 任一前置条件不满足时：自动回退至现有 `eog_ecg_correlation`，保证 `REF` / 双极 / 非标准通道录音 100% 正常工作且 UI 透明告知原因。

---

### 2.3 7 类标签映射与保守推荐策略

`mne-icalabel` 输出的 7 类固定英文标签与前端中文展示映射：

| 英文标签 (`iclabel_label`) | 中文显示名 | 类别属性 | 是否参与自动预勾选 |
|---|---|---|---|
| `brain` | 大脑脑电 | 有效信号 | 否（永不自动预勾） |
| `eye blink` | 眼电伪迹 | 伪迹 | 是 |
| `muscle artifact` | 肌电伪迹 | 伪迹 | 是 |
| `heart beat` | 心电伪迹 | 伪迹 | 是 |
| `line noise` | 工频噪声 | 伪迹 | 是 |
| `channel noise` | 单导噪声 | 伪迹 | 是 |
| `other` | 其他成分 | 不确定 | 否（交由人工结合热力图判断） |

**保守预勾选规则（防止误删真实脑电）**：
- 当 `classifier_status == "ready"` 时，仅满足以下**全部条件**的成分标记 `recommended_exclude = True`：
  1. `iclabel_label in {"eye blink", "muscle artifact", "heart beat", "line noise", "channel noise"}`；
  2. `iclabel_probability >= 0.75`（可作为常量/配置项，明确写入证据）；
  3. `iclabel_probabilities["brain"] < 0.25`（防止脑电混合成分被误删）。
- 若同时配置了**专用 EOG / ECG 物理通道**（`eog_channel_source in {"dedicated_eog", "explicit_channel"}` 或存在 `ecg_channels_used`），高置信度相关检出也可并入推荐，并在每条成分的 `recommendation_reasons` 数组里明确列出触发原因（如 `["iclabel:eye_blink:0.94"]` 或 `["iclabel:channel_noise:0.89"]`、`["eog_correlation:0.86"]`），完全可解释、可追溯。

---

### 2.4 指纹与缓存版本升级（防止模型漂移）

- [ica_review_cache.py](../../backend/app/services/ica_review_cache.py)：
  - 将 `compute_ica_fit_fingerprint` 版本从 `"ica-fit-v2-source-references"` 升级为 **`"ica-fit-v3-extended-icalabel"`**；
  - 指纹输入新增：`ica_extended: True`、`fit_low_hz`、`fit_high_hz`、`classifier_version`（使新旧模型绝不发生缓存串用）。
  - 仍排除 `ica_exclude_components` 和 `ica_fit_fingerprint`，确保同一套已审查模型改选不同成分时 100% 命中缓存复用。
- [artifact_removal.py（contracts）](../../backend/app/scientific/contracts/artifact_removal.py)：
  - 将 `ARTIFACT_REMOVAL_APPLICATION_VERSION` 升级为 **`"artifact-removal-application-v4-extended-icalabel"`**，确保 `AnalysisPreprocessCache` 与历史分析运行溯源严格区分新旧 ICA 实现。

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
3. **步骤弹窗文案**（[ArtifactRemovalStepDialog.xaml](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Views/Preprocessing/ArtifactRemovalStepDialog.xaml)）：
   - 更新说明文案：明确 `Infomax ICA` 已升级为 `Extended Infomax`，在平均参考（CAR）下自动启用 ICLabel 7 类伪迹识别（眼电/肌电/心电/单导噪声/工频），主分析频段仍保持 `1–50 Hz`。

---

## 5. 模块拆分与文件规模约束（严格守住 400 行红线）

> **重要提醒**：`backend/app/scientific/artifact_removal.py` 当前已 **421 行**，禁止继续往该文件堆砌 ICLabel 逻辑！必须按职责拆出独立模块。

| 层级 | 文件路径 | 变更类型 | 职责与行数目标 |
|---|---|---|---|
| 后端依赖 | `backend/pyproject.toml` | 修改 | 新增 `mne-icalabel`、`onnxruntime`（无 `torch`） |
| 后端科学 | `backend/app/scientific/ica_classifier.py` | **新增** | 封装准入条件门控、3D Montage 绑定、ONNX 后端 `mne-icalabel` 推理、7 类概率格式化、保守推荐判定与优雅降级（目标 ≤ 220 行） |
| 后端科学 | `backend/app/scientific/artifact_removal.py` | 修改 | 升级为 `fit_params={"extended": True}`，支持接收可选宽带拟合输入与 `signal_provenance`，调用 `ica_classifier.py` 合并打分与推荐，**控制行数不增长或缩减至 ≤ 400 行** |
| 后端契约 | `backend/app/scientific/contracts/artifact_removal.py` | 修改 | 升级 `ARTIFACT_REMOVAL_APPLICATION_VERSION` |
| 后端服务 | `backend/app/services/ica_review_cache.py` | 修改 | 升级指纹版本至 `ica-fit-v3-extended-icalabel` 并纳入宽带拟合与分类器版本字段 |
| 后端服务 | `backend/app/services/mapped_preprocessing.py` | 修改 | 当启用 `ica_infomax` 且缓存未命中时，在释放 `source` 前为 ICA 构造临时宽带副本（`1–min(100, sfreq/2-1) Hz` + 同坏段掩膜） |
| 后端服务 | `backend/app/services/artifact_inspection.py` | 修改 | 整段 ICA 审查复用同一宽带拟合路径，返回新增分类器与 7 类概率字段 |
| 桌面契约 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/Contracts/PreprocessingContracts.cs` | 修改 | 扩展 `IcaInspectionResponse` 与 `IcaComponentItem` 新增字段 |
| 桌面模型 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Preprocessing/ReviewPreprocessingModels.cs` | 修改 | 卡片中文标签、概率百分比、7 类分布 Tooltip 与推荐原因 |
| 桌面视图 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Views/IcaReviewDialog.xaml` & `.xaml.cs` | 修改 | 展示分类器状态/回退提示、7 类中文标签与筛选交互（≤ 200 行） |
| 桌面视图 | `desktop-client/BrainPlatform.Desktop/Modules/Review/Views/Preprocessing/ArtifactRemovalStepDialog.xaml` | 修改 | 同步 Extended Infomax + ICLabel 说明 |

---

## 6. 分阶段实施任务清单

### 阶段 1：依赖接入与离线能力验证
- [ ] **1.1** 更新 `backend/pyproject.toml`，在 `.venv` 中安装 `mne-icalabel` 与 `onnxruntime`（确认未引入 `torch`）。
- [ ] **1.2** 编写离线冒烟测试，验证 `mne_icalabel.label_components(..., method="iclabel")` 使用 `onnx` 后端在无网络访问下可正常输出 7 类概率与标签，实测 15–20 成分 CPU 推理耗时 < 1 秒。

### 阶段 2：后端 `ica_classifier.py` 与准入/降级契约（TDD）
- [ ] **2.1** 新增 `backend/tests/test_ica_classifier.py`，先写失败测试覆盖：
  - CAR + 有效 3D 坐标 → `classifier_status == "ready"`，输出完整 7 类概率（总和 ≈ 1.0）；
  - `REF` 单参考 → `classifier_status == "fallback_eog_ecg"`，reason=`non_average_reference_montage`，绝不强跑模型；
  - 双极导联（如 `Fp1-F7`）→ 明确回退，reason=`non_average_reference_montage`；
  - 缺失 3D 坐标或采样率 < 104 Hz → 明确回退对应原因码；
  - 保守预勾选规则：高置信度 `eye blink / muscle / heart / channel noise / line noise`（≥ 0.75 且 `brain < 0.25`）被推荐；`brain` 或 `other` 永不被自动预勾选。
- [ ] **2.2** 实现 `backend/app/scientific/ica_classifier.py` 并通过全部单测。

### 阶段 3：宽带拟合旁路、Extended Infomax 与指纹/缓存升级
- [ ] **3.1** 在 `artifact_removal.py` 升级为 `fit_params={"extended": True}`，支持由调用方传入宽带拟合信号矩阵（`fit_values_v`）与 `signal_provenance`，确保空间解混矩阵仅应用回 `1–50 Hz` 主信号。
- [ ] **3.2** 更新 `mapped_preprocessing.py` 与 `artifact_inspection.py`：仅在启用 `ica_infomax` 时从 `source` 迁出临时 `1–min(100, sfreq/2-1) Hz` 宽带副本，同步施加相同坏段 `NaN` 掩膜，拟合完毕立即释放临时 mmap。
- [ ] **3.3** 升级 `ica_review_cache.py` 指纹版本与 `ARTIFACT_REMOVAL_APPLICATION_VERSION`，确保同一指纹在「ICA 审查 → 科学对比预览 → 下游 PSD/RBP/FAA」全链路 100% 缓存命中复用。
- [ ] **3.4** 新增后端集成回归测试（`backend/tests/test_ica_label_integration.py`）：
  - 合成含眨眼 + 局部单通道脉冲 + 高频肌电的 CAR EEG，验证 ICLabel 检出对应伪迹且主输出仍严格处于 `1–50 Hz`；
  - 验证坏段 `NaN` 缺口与 `artifact_removal_validity` 无回归；
  - 验证已有 `test_offline_artifact_removal*.py`、`test_paired_scientific_preview.py`、`test_artifact_report_consistency.py` 全部通过。

### 阶段 4：桌面端契约、审查弹窗与回归测试
- [ ] **4.1** 同步 `PreprocessingContracts.cs` 与 `ReviewPreprocessingModels.cs`，保留对旧字段兼容（可空）且正确渲染中文 7 类标签和 7 类概率分布 Tooltip。
- [ ] **4.2** 更新 `IcaReviewDialog.xaml` / `.xaml.cs` 与 `ArtifactRemovalStepDialog.xaml`：保持严格 3 色克制风格，顶部呈现 ICLabel 就绪或回退原因，卡片呈现中文标签与置信度，支持「全部 / 仅看建议剔除」切换与「恢复系统推荐」。
- [ ] **4.3** 新增 WPF 单元测试（覆盖 `ready` 7 类标签展示、Tooltip 格式化、`fallback_eog_ecg` 优雅回退显示、保守推荐重置及既有 `IcSparklineControl` 契约零回归）。

### 阶段 5：全量回归与真实录音验收
- [ ] **5.1** 后端全量预处理/频谱测试通过；桌面端 Release 编译 0 警告 0 错误，相关 WPF 测试全通过。
- [ ] **5.2** 使用同一份真实录音分别在 **平均参考（CAR）** 与 **硬件参考（REF）** 下执行端到端验收：
  - CAR 下：验证 ICLabel 成功识别眼电、单导局部伪迹（如 IC14 类成分）及肌电，展示清晰中文概率标签，确认剔除后科学对比与 PSD/RBP 正常复用指纹；
  - REF 下：验证明确提示切换 CAR 启用 ICLabel，并无缝回退到 EOG/ECG 相关系数且不报错。
- [ ] **5.3** 执行文件规模检查（≤ 400 行软上限）、`pip list` 确认无 `torch`、`git diff --check`，形成匿名验收报告（不提交真实录音或私有日志）。

---

## 7. 最终验收红线（Acceptance Checklist）

验收时逐条核对以下硬指标，任一条不满足即退回修改：

1. **零重型依赖**：`backend/.venv` 与 `pyproject.toml` 中绝对不含 `torch`；仅使用 `mne-icalabel` + `onnxruntime` CPU 推理；断网离线可运行。
2. **主链路零污染**：下游 PSD / RBP / FAA / 科学对比预览的输出信号仍严格维持 `1–50 Hz + 50 Hz 陷波`，不受 `1–100 Hz` 宽带拟合副本影响。
3. **严禁跨参考混算**：非 CAR（`REF` / 双极）录音绝不偷偷重参考拟合再应用回原参考，必须显式回退并下发结构化原因码与中文提示。
4. **保守不误删**：`brain` 与 `other` 成分绝不自动预勾选；最终剔除成分必须经人工审查弹窗确认并受 `ica_fit_fingerprint` 严格绑定。
5. **缓存与缺口零回归**：同一指纹在审查、配对科学对比预览与下游算法间 100% 命中复用；坏段 `NaN` 掩膜与 `artifact_removal_validity` 保持完整。
6. **UI 审美与规模红线**：保持严格 3 色风格（中性灰白 + 品牌蓝 + 暖琥珀）；所有新增/改动业务文件遵守 400 行软上限；Desktop Release 0 警告 0 错误；前后端新增与既有回归测试 100% 通过。
