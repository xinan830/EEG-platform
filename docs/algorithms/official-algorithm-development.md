# 官方算法接入与维护指南

本文对应当前实现。规范行为以 `openspec/specs/analysis/` 为准；能力状态见 [管理基线](official-algorithm-management.md)，清理和验收证据见 [本轮收尾记录](../validation/algorithm-boundary-cleanup-acceptance.md)。本指南只讨论官方算法执行，不改变阅图、采集、报告或用户定义历史存储的独立边界。

## 所有权与调用链

```text
WPF 参数 / 导联选择
  -> Run 或 analysis-windows 请求
  -> 共享队列 / 窗口调度 / 当前模块解析
  -> 模块声明的信号请求 -> recording context -> 读取 / 导联投影 / 预处理
  -> 模块独立科学计算 -> typed result
  -> Runtime 校验 -> 通用序列化 -> Run / immutable artifact
  -> 当前格式读取校验 -> WPF handler / preview
```

| 职责 | 权威位置 | 不应承载的职责 |
| --- | --- | --- |
| 身份、参数、输入与输出声明 | `backend/app/algorithms/<id>/manifest.py`、`config.py` | UI 状态、SQL、HTTP |
| 公式、算法质量门、特有证据 | 同包 `compute.py`、`official.py`、`runner.py`、`window.py` 等 | artifact 写入、旧结果解析 |
| 内置模块组合 | `backend/app/algorithms/builtin_modules.py` | 根据 ID 拼接科学公式 |
| 输入请求/数据、结果类型、校验 | `backend/app/algorithm_runtime/{contracts,signal_source,result_validation}.py` | 导入具体算法或服务实现 |
| 记录到 typed 输入的适配 | `backend/app/services/algorithm_recording_context.py` | 改变模块声明的滤波和窗口规则 |
| 全量/按需/组合调度、身份和取消 | Run queue、dynamic-window/reuse 服务及 WPF 共享协调器 | 复制算法实现、默换策略 |
| 结果序列化与数组元数据 | `backend/app/services/run_result_serialization.py` | 按算法 ID 解释矩阵或专属证据 |
| Run、artifact 与 provenance 持久化 | Run/queue、artifact 服务及 persistence repositories | 修改原始 EEG |
| 界面工厂与输出消费 | WPF `AlgorithmUiRegistry`、`AlgorithmRunResultHandlerRegistry`、各 detail/handler | FFT、PSD、band power 或算法公式 |

新增采用现有输入/输出形状的算法通常只需算法包、工厂注册、WPF 两个注册点和对应测试。新数据源或新结果形状确实可能需要扩展公共契约；不能承诺公共层永远不变，但扩展应按能力/类型，不按算法 ID 加科学分支。

## 输入与预处理

`SignalRequirements` 声明 `input_roles`、`representation`、`source_shape`、`preprocessing_scope` 和缺失样本策略。模块将选择解析为角色到实际导联输出的映射，构造 `SignalSourceRequest` 或 `SpectralSourceRequest`；context 校验采样率、范围、映射和 montage fingerprint 后才读取。

| 算法 | 输入 | 处理范围与计算所有权 |
| --- | --- | --- |
| PSD | 所选分析通道的 spectrum | 配置连续预处理后切片；Welch 及结构化输出语义由模块声明 |
| STFT | 所选分析通道的 spectrogram | 配置连续预处理后切片；使用独立谱图服务与时频轴契约 |
| Peak Frequency、IAPF、Band Ratio、Theta/Beta | 所选分析通道的 Welch spectrum | 经 typed Welch adapter；处理范围由各模块声明，各自负责峰值、频带积分/比值等科学规则 |
| RBP | 所选分析通道的 Welch spectrum | 固定连续预处理后切片；频带积分和相对功率规则归模块所有 |
| FAA | 按声明顺序的 F3/F4 角色时序 | 配对读取、声明的连续预处理、paired-epoch FFT 和配对质量门保持独立 |
| Brainbeat | 显式 Fz/Pz 角色的频谱 | 模块按窗声明预处理与频谱需求，独立计算 IAPF 及比值；不隐式共享跨窗 EMA |

时序数据是 `(samples, channels)`、V/float64；`channel_order` 与数组列、角色映射一致。频谱边界显式记录服务 uV²/Hz 与算法 V²/Hz 的换算。请求使用绝对样本边界和 Hz 采样率；秒到样本的既有取整规则需在测试中精确比较，不能以浮点容差掩盖一帧偏移。

原始记录不可变。切换导联只选记录支持且后端校验过的分析快照；请求携带快照，计算输入、通道选项、cache identity 和 provenance 一起更新。没有相应原始源通道时返回失败，不替换为别的通道。NaN/gap 由声明策略保留或拒绝，模块按自己的质量规则处理。

不能把“选择输出 1–30 Hz”当成“已经做 1–30 Hz 预滤波”：滤波范围和输出频率范围分别声明。连续滤波、逐窗处理和无软件滤波也不能互换。

连续准备缓存绑定源 checksum、通道顺序/单位、采样率、导联、filter/notch、处理范围/边界与实现身份；Run/window 再绑定具体样本范围、配置和结果版本。显示范围、缩放不属于科学身份。缓存命中必须有完整身份相等证据。

## 输出与版本

`AlgorithmOutputContract` 必须声明静态/动态类型、字段/轴单位与形状、角色次序、quality/state、失败语义和 evidence 键。模块可返回：

| 静态 / 动态类型 | 使用场景 | 存储 |
| --- | --- | --- |
| `AlgorithmResult` / `AlgorithmSeriesResult` | 七种标量指标；RBP 使用明确声明的五频带 `output_values` | canonical `output.value` 或当前多字段输出；动态点携带显式状态 |
| `AlgorithmStructuredResult` / `AlgorithmStructuredSeriesResult` | PSD/STFT 矩阵、轴及逐窗结构化输出 | 大数组保存 NPZ；JSON 保存轴/形状/单位与 artifact 引用 |

Runtime 在发布前校验 shape、轴、单位、角色、并行列表、数值可用性与 quality/failure。公共 serializer 只分发结果类型，模块专属证据通过声明的 extension/projector 提供。算法不得直接持久化文件，也不得把整块矩阵塞进 summary。

当前根版本为 `official-algorithm-result-v1`，标量动态点/summary 为 `dynamic-analysis-frame-v6`。每点状态显式为 `Partial`、`Complete`、`Rejected` 或 `Unavailable`，不从 warmup/quality 推导。`allow_warmup` 是当前 Partial 窗口规划选项，不是已退休的结果字段。Unavailable 数值用 `null` 加原因；RBP 的主值为空时仍可提供声明的 `band_values`，不能按一个通用标量是否为空来判断整个算法失败。

发布、Run/result 读取、预览、导出、provenance 和 WPF 都使用当前契约；无效已完成结果统一 HTTP 409 / `ALGORITHM_RESULT_INVALID`。只维护显式 current 的官方科学版本和当前结果格式，不提供旧格式 reader、转换、双写、重算或专属 UI。既有 Run/artifact 和原始 EEG 不迁移、不删除。通用错误封装、数据库 migration 和服务接口适配是另外的范围，不能据此声称整个项目“零兼容”。

## 新增算法步骤

1. 在 OpenSpec 冻结公式依据、版本、参数、角色、单位、取整、窗口、滤波、质量/失败与 provenance。科学变更单独立项，不能混入结构整理。
2. 实现算法包；manifest 提供 current、实现身份、`SignalRequirements`、`AlgorithmOutputContract`、参数和策略声明。优先复用已有 typed 数据能力；公式和专属证据留在模块。
3. 在唯一工厂表注册。验证 current 唯一性、catalog/manifest parity、无具体算法依赖进入通用 Runtime。通用序列化不新增 ID 分支。
4. 分别声明 full、on-demand、combined 和两个方向的复用。支持某模式不自动证明 combined 或复用正确；未支持明确给原因，未验证保持 `unvalidated`。
5. 接入 WPF UI registry 与 result-handler registry，完成请求序列化、当前版本校验、图表/指标、实际范围、状态、失败与 provenance 展示。前端不重算科学公式。
6. 添加独立数值参考、固定输入和声明容差；逐点比较数组与中间量，精确比较轴/样本/状态/单位/科学身份。大数组单独保存受控 checksum artifact，不覆盖原冻结件。
7. 验证静态、动态全量、按需、combined，以及所声明的方向性复用；包括导联切换、参数改变、缺失/坏段、取消和旧请求竞态。
8. 更新管理矩阵、作者指南和 tasks，运行后端/WPF 套件、构建、规范与规模检查。实际桌面验收和自动化证据分别记录，验收通过才提交推送。

## 排查入口

从用户动作追 `request_id`、Run/window ID 和参数快照，先确认导联 snapshot、有序分析通道、采样率、绝对样本范围与处理身份；再看算法版本/实现和 quality/failure，最后核对 artifact checksum、轴、单位及数值。

输入不同优先查 source adapter/导联/预处理；输入相同而科学结果不同查模块；结果正确而界面不对查 WPF handler/preview。复用只在身份和声明方向均匹配时成立。用独立参考或冻结数组判断数值，不以图形“看起来正常”判断正确。
