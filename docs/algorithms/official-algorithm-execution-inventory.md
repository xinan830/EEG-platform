# 官方算法执行边界清单

本文记录 `clarify-algorithm-execution-boundaries` 的初始盘点及第 2–4 章整理结果。九算法已接入 typed 信号边界和当前结果契约；下文保留明确标注的初始路径以说明整理依据。策略能力和验证状态以
[官方算法管理基线](official-algorithm-management.md) 及其机读矩阵为准。

## 统一执行骨架

```text
WPF AlgorithmListViewModel
  -> POST /api/runs 或 analysis-windows ensure
  -> Runs / DynamicWindowService
  -> RunQueue / OnDemand worker
  -> AlgorithmRuntime + registry
  -> builtin_modules.py 的唯一九算法工厂表
  -> 每算法 runner.execute_static / execute_dynamic / execute_window
  -> typed Algorithm*Result
  -> RunAnalysisExecutor / bounded window result contract
  -> SQLite Run summary + immutable artifact + provenance
  -> WPF result-handler registry / preview adapter
```

共享层拥有请求校验、current 版本解析、策略/窗口调度、生命周期、缓存和复用身份、结构校验、artifact 持久化及通用 provenance。公式、算法参数、输入角色、算法专属质量门和 evidence 由 `backend/app/algorithms/<id>/` 所有。`builtin_modules.py` 是后端模块的单一组合点；WPF 另有 UI/detail registry 和结果 handler registry。

## 九算法执行入口归属

| 算法 | 模块及数值实现 | Full 静态/动态 | On-demand 单窗 | 输出家族 |
| --- | --- | --- | --- | --- |
| PSD | `algorithms/psd/{runner.py,config.py}` | `PsdAlgorithm.execute_static/execute_dynamic` | runner 的 `execute_window` | structured / structured-series |
| STFT | `algorithms/stft/{runner.py,window.py}` | `StftAlgorithm.execute_static/execute_dynamic` | `StftWindowCapability.execute_window` | structured / structured-series |
| RBP | `algorithms/rbp/{runner.py,window.py,official.py}` | `RbpAlgorithm.execute_static/execute_dynamic` | `RbpWindowCapability.execute_window` | scalar / series |
| Band Ratio | `algorithms/band_ratio/{runner.py,compute.py}` | `execute_static/execute_dynamic` | `ScalarDynamicWindowCapability.execute_window` | scalar / series |
| Peak Frequency | `algorithms/peak_frequency/{runner.py,compute.py}` | `execute_static/execute_dynamic` | `ScalarDynamicWindowCapability.execute_window` | scalar / series |
| IAPF | `algorithms/iapf/{runner.py,official.py}` | `execute_static/execute_dynamic` | `ScalarDynamicWindowCapability.execute_window` | scalar / series |
| Theta/Beta | `algorithms/theta_beta/{runner.py,compute.py}` | `execute_static/execute_dynamic` | `ScalarDynamicWindowCapability.execute_window` | scalar / series |
| FAA | `algorithms/faa/{runner.py,window.py,official.py}` | `execute_static/execute_dynamic` | `FaaWindowCapability.execute_window` | scalar / series |
| Brainbeat | `algorithms/brainbeat/{runner.py,window.py,official.py}` | `execute_static/execute_dynamic` | `BrainbeatWindowCapability.execute_window` | scalar / series |

## 当前信号输入路径

Run 的算法配置先由 `Runs` 固化，`RunAnalysisExecutor` 建立 `_RecordingAlgorithmContext`，模块的 `resolve_inputs` 解析算法配置指定的角色/通道。按需窗口由 `DynamicWindowService` 和模块窗口能力规划；其 `AlgorithmInputs` 仍引用同一 bounded context。具体读取路径目前有分叉：

| 路径 | 调用者/算法 | 导联与预处理 | 范围及缺失语义 | provenance |
| --- | --- | --- | --- | --- |
| `load_spectrum` | PSD、Band Ratio、Peak Frequency、IAPF、Theta/Beta；Brainbeat window 用按需频谱 | `SpectralAnalysisService`；底层 `mapped_preprocess` 通过 recording sample reader 取得选定导联投影，再连续滤波/陷波；各算法的频带与窗口参数仍分别声明 | 以秒请求；起点 `floor(start_s * sfreq)`，长度 `round(window_s * sfreq)` 后限幅；底层 acquisition reader 将 NaN gaps 保留，科学质量门处理不可用/坏段 | 光谱 payload 含 signal source、导联/reference、filter contract、units、质量与频轴；context 添加频谱证据 |
| `load_spectrogram` | STFT static/dynamic 与部分结构化分析 | 同一 recording service / 连续预处理能力，但专属 spectrogram 参数与投影 | 谱图读取也以秒转换样本；需与 `spectral_analysis.py` 和其测试逐项核对是否与 PSD 使用完全相同边界 | 返回谱图 contract、频率/时间轴、filter/reference 信息 |
| `load_faa_signals` | FAA static/dynamic/按需 | 按角色取 F3/F4 配对；配置滤波时以 `mapped_preprocess` 对整段 recording 连续预处理后再切窗；无配置滤波时用 sample reader 直接取原始/已投影信号 | `floor(start_s * sfreq)` 起点，`round(end_s * sfreq)` 终点并受时长限制；公式再按 2 秒 epoch / 1 秒 hop 检查成对数据与质量门 | 单独记录左右源、实际/请求范围、连续预处理范围和 FAA epoch contract |
| `load_spectrum` per-window | Brainbeat on-demand | 每个 planned window 请求 Fz/Pz；由各窗频谱调用共享 spectral service，再由 Brainbeat 独立计算 IAPF-driven ratio | 使用计划窗口的 start 与 duration；策略规划在共享窗口计划器，缺数据由能力返回 insufficient/unavailable | Brainbeat 能力记录双角色、独立窗口、无跨窗 EMA 等专属 provenance |

导联投影由 `AcquisitionMontageSampleReader` 根据记录绑定或通过后端校验的分析快照从原始采集通道构造输出，样本以 V/float64、sample-major 读取，原始文件保持不变。`AnalysisMontageSnapshots` 负责候选校验和按 recording 保存内容寻址快照。初始实现跨越 recording service、reader、preprocess cache 和算法 context；第 2 章已将 role、样本边界、缺失策略和 preprocessing provenance 聚合到强类型 bounded source request/result。

FAA full 和 on-demand 当前都先构造 `SignalSourceRequest`，由 `_RecordingAlgorithmContext.load_signal_source` 校验采样率、范围及 raw/montage identity，再委派到 FAA 原有连续预处理读取实现并包装为 `SignalSourceData`。PSD 通过 `SpectralSourceRequest(representation="spectrum")` 绑定 Welch 请求的样本范围、角色/通道顺序、导联身份、连续滤波、陷波和频率输出范围；STFT 使用同一类型但明确 `representation="spectrogram"`，仍委派到独立 spectrogram 服务，不将其伪装成 Welch 输出。Brainbeat 的 source adapter 显式声明 Fz/Pz 语义角色并映射到当前通道，所有执行策略共用这条配对 typed 请求。Band Ratio、Peak Frequency、IAPF、Theta/Beta、RBP 通过 context 的 typed Welch adapter 读取，具体指标公式仍归各自模块。迁移组在算法证据中保留 typed request；计算公式、坐标和质量门保持原状。FAA 独立参考、Brainbeat 独立参考以及 PSD/STFT 数值参考和聚焦回归通过；底层 FAA 读取函数暂留为适配器内部实现。

此表保留初始代码路径盘点。第 2 章已完成九算法 typed 输入接入及聚焦契约回归；这不表示所有导联、滤波、缺失和范围组合都已穷举，完整交付回归及人工验收仍在第 5 章。

Combined preview/background-full scheduling and full/on-demand directional reuse are coordinated by shared WPF and backend services; algorithm support is declared in the manifest and `DynamicStrategyCapabilities`. Those paths do not add separate scientific formulas. Exact implemented/validated/unsupported states remain in `official-algorithm-strategy-matrix.json` and `official-algorithm-management.md`.

## 当前结果字段与兼容候选

## typed result 实际映射（3.1）

下表按当前 runner 返回对象及 `RunAnalysisExecutor` 的消费分支映射；manifest 的 typed `output_contract` 已在 3.2 完成。静态/动态是算法执行模式；structured family 表示数值矩阵/轴由 artifact 保存，标量 family 则逐点序列化为 Run summary。

| 算法 | 静态返回类型 | 动态返回类型 | 输出形状与边界说明 |
| --- | --- | --- | --- |
| Band Ratio | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个无量纲比例；动态为时间点标量序列 |
| Brainbeat | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个 Fz/Pz 比值；动态点携带窗状态与证据 |
| FAA | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个 F3/F4 配对不对称标量；动态点有配对 epoch 证据 |
| IAPF | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个 Hz 标量；动态为时间点标量序列 |
| Peak Frequency | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个 Hz 标量；动态为时间点标量序列 |
| Theta/Beta | `AlgorithmResult` | `AlgorithmSeriesResult` | 每次一个无量纲比例；动态为时间点标量序列 |
| RBP | `AlgorithmResult` | `AlgorithmSeriesResult` | 五频带占比放在 `output_values`/`band_values` 中，主值为空；现有 scalar contract 的多指标扩展由 typed output contract 明确声明字段、单位和次序，无需第五种 result type。 |
| PSD | `AlgorithmStructuredResult` | `AlgorithmStructuredSeriesResult` | 频率轴及 PSD/频带矩阵；动态结构化窗口首轴对应 `windows`，数组与轴带单位 |
| STFT | `AlgorithmStructuredResult` | `AlgorithmStructuredSeriesResult` | 时间/频率轴及线性、dB 时频矩阵；动态数组首轴对应 `windows` |

`AlgorithmStructuredSeriesResult` 的每窗状态、质量、失败、证据和 provenance 是 `StructuredSeriesWindow` 的显式字段；标量动态结果则由 `AlgorithmSeriesResult` 承载逐点时间、窗口、quality/failure、state/evidence。静态两类都显式承载 requested/actual range、quality/failure 和 evidence。四种现有类型覆盖九个模块实际返回形状；当前没有证据支持新增第五种类型。初始输出声明与 RBP 多指标映射缺口已由 3.2 完成，当前格式按第 4 章规则读取。

### 模块输出声明（3.2）

九个 builtin manifest 现在都提供 typed `output_contract`，并随 `/api/algorithms` 目录返回。它从模块已拥有的字段描述和 signal role 声明构成，明确静态/动态 result family、字段单位/形状、窗口轴、角色顺序、可见质量值、动态 state enum、失败值规则，以及公共证据键和算法专属证据键。注册契约测试要求每个 builtin 都提供声明，并验证字段与既有 manifest 输出字段、角色顺序、质量/state 及证据公共键一致。此声明只描述接口，不搬移或统一算法公式、质量阈值或证据生成责任。

算法专属证据键：Band Ratio=`band_ratio_evidence`/`sample_coordinate`；Brainbeat=`brainbeat_evidence`/`paired_role_order`/`window_source`；FAA=`faa_evidence`/`paired_epoch_quality`/`preprocessing_range`；IAPF=`iapf_evidence`/`peak_hz`/`cog_hz`/`estimate_source`；Peak Frequency=`peak_frequency_evidence`/`sample_coordinate`；Theta/Beta=`theta_beta_evidence`/`iapf_hz`/`theta_range_hz`/`beta_range_hz`；RBP=`bands`/`window_s`/`denominator`/`band_powers`；PSD=`welch_parameters`/`band_share`/`sample_coordinate`；STFT=`spectrogram_contract_version`/`inner_valid_count`/`padding_policy`。这些键是模块声明的能力范围，不表示每个结果都必然产生每个键；结果可用性仍以实际 evidence payload 和 quality/failure 为准。

| 位置 | 初始观察 | 第 4 章处理结果/范围 | 证据 |
| --- | --- | --- | --- |
| `algorithm_runtime/executor.py` | 从 quality/failure/warmups 推导缺失 states | 已删除；七个标量 producer 显式提供 states，缺失或长度不一致失败 | runtime/result boundary 契约测试 |
| `services/run_result_serialization.py` | 再次推导 state，双写 warmup 和顶层 value | 已删除；仅写 output.value，点级 analysis_state 直接取模块 states | 冻结 36 场景回归及 current-contract API 测试 |
| `algorithm_runtime/contracts.py` | warmups/states 双列表带默认值 | warmups 已删除，states 必填；未知 warmups 字段被拒绝 | `test_algorithm_runtime_contracts.py` |
| `algorithm_runtime/windows.py` | Window/Frame 的 warmup 便捷属性 | 已删除；直接使用 state；allow_warmup 仍控制 Partial 窗口计划，是当前科学能力 | 全量、按需与 combined 策略回归 |
| `services/artifacts.py` | prepare_window_index 通用 sidecar 预备工具 | 不属于本次结果 reader；未接入 retired Run 读取/重建路径，没有新增调用者 | 生产代码检索、当前窗口读取 API 测试 |
| `services/runs.py` | 当前结果缓存身份使用独立 evidence 版本 | 统一引用 official-algorithm-result-v1 常量，避免命中旧格式缓存 | 队列/current-contract 测试 |
| `services/recordings.py` | spectrum/spectrogram 转发到 SpectralAnalysisService | 当前输入服务委派，不是旧结果格式 reader；保持当前职责 | typed signal-source 测试 |
| WPF previews / metric parsers | output.value 与顶层 value、单位、质量存在多形状回退 | 已删除退休字段读取；HTTP 和结果投影先验证当前契约，使用显式状态/单位 | `CurrentAlgorithmResultTests` 及九算法详情/窗口测试 |
| `ResultService` / provenance / Run API | 未统一约束 summary 版本 | 新增当前 typed schema；持久化前和读取/预览/导出/溯源入口校验，不转换失败载荷 | `test_current_algorithm_result_contract.py` |
| Run ensure API | 无 enqueue 时同步 create | 本章范围外的服务接口适配；生产 main 装配持久队列，非旧结果 reader | `api/runs.py`、`main.py` |
| Database migrations | SQLite 旧表名/默认值迁移 | 本章范围外的启动/存储结构演进，不执行旧算法或解释旧结果；未修改存储字节 | `persistence/migrations.py` |
| 旧 recording analysis API | 文档提及已不存在的路由 | 代码没有该路由；说明应以当前标准 Run API 为准 | `api/recordings.py` |

## 现有 baseline 与证据覆盖

仓库已有独立数字参考冻结：`docs/validation/official-algorithm-numeric-baseline.json` 指向 NPZ checksum，记录 NumPy/SciPy 版本与 `rtol=1e-10`、`atol=1e-20`；它覆盖独立信号、Welch/STFT 核心数组及九个算法参考测试身份。另有 `official-algorithm-strategy-baseline.json/.npz` 冻结九算法动态 Run 数组/series JSON，以及 `official-algorithm-strategy-matrix.json` 冻结 manifest/schema 指纹和策略验证引用。每份明确仅属工程验证。

这些现有冻结件可作为结果/数值起点，但不完全满足本 change 的全部基线要求：它们不是每条策略、每种 montage/filter/range/error/provenance 组合的独立输入快照；没有为全部九算法冻结各自 source checksum 和每一种失败场景；也不证明所有 artifact 逐字段与历史可达 Run contract 一致。输入调用链和兼容候选按上文记录；缺失覆盖不能据此假定为已验证。本次不重生成或改写这些冻结数组，以免把新输出误当成整理前基线。

## 第 4 章整理结论

官方结果当前仅接受 `official-algorithm-result-v1`；标量动态 summary/point 使用 `dynamic-analysis-frame-v6`。状态回退、warmup 双写和顶层重复 value 已删除，WPF 与后端同步收敛。未增加历史格式 reader、转换器、双写或旧格式专属展示。当前 schema 不合法统一报 `ALGORITHM_RESULT_INVALID`，不按具体旧格式分类。已保存字节与原始记录不迁移、不删除。Partial 窗口仍由算法策略声明，数据库结构迁移和通用服务接口适配不在本章删除范围；不能由此宣称全项目不存在任何接口兼容逻辑。

本清单不授权改变算法科学行为。第 2–4 章完成输入/输出边界、当前结果版本和 WPF parser 整理。当前边界和接入步骤见 [作者指南](official-algorithm-development.md)，交付状态见 [第 5 章收尾记录](../validation/algorithm-boundary-cleanup-acceptance.md)。第 4 章验证入口见 [当前结果契约验证](../validation/current-algorithm-result-contract-validation.md)。

## 结果边界实现与回归（3.3 / 3.4）

`algorithm_runtime/result_validation.py` 统一校验声明的结果类型、字段/单位、角色顺序、并行序列长度、质量/失败、结构化维度和轴；`services/run_result_serialization.py` 负责四种结果类型的序列化及公共数组元数据。`RunAnalysisExecutor` 保留结果类型分发；FAA 专属 provenance 解释迁至 `algorithms/faa/provenance.py`，由模块能力投影接入。同步/队列执行保存 `ALGORITHM_RESULT_INVALID`，不将输出契约错误当作输入无效。

独立于真实记录的 36 场景冻结回归覆盖九算法静态/动态正常及高幅值输入，对照完整 summary、provenance、artifact 数组及元数据；动态正常输入另与原有 21 个冻结数值数组比较。结果及明确豁免的存储分配身份见 `docs/validation/algorithm-result-boundary-validation.md`。这不是全部导联/滤波组合的穷举验证，也不是桌面验收；第 4 章状态契约迁移不能据此省略。
