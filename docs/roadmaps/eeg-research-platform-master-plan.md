# 可复现、可配置、可扩展脑科研平台：完整产品与技术路线计划

> 文档定位：完整目标版本蓝图（Target Architecture + Product Roadmap）  
> 适用对象：产品负责人、算法工程师、前后端工程师、科研合作方、定制交付团队  
> 核心定位：平台底座 + 科研计算积木 + 行业算法模块 + 用户自定义算法 + 定制开发能力  
> 非定位：不是把若干临床指标写死在页面里的单体软件  
> 原则：优先科研可复现、可解释、可验证、可扩展；临床有效性必须独立验证，不以工程实现替代医学结论

---

## 1. 产品愿景

本平台的目标不是简单做一个“EEG 查看器”或“固定算法分析软件”，而是构建一个面向脑科研的可复现计算平台：

**数据进入平台后，通过统一、可审计的底层信号处理能力，产出标准科研积木；科研用户可以基于这些积木配置参数、组合公式、创建新指标、验证算法、比较版本，并最终形成可复现、可分享、可定制的算法模块。**

平台的长期形态：

```text
EEG / 生理数据
    ↓
可信计算底座
    ↓
标准科研积木
    ↓
参数化算法编排
    ↓
算法版本 / 配置指纹 / 验证报告
    ↓
科研分析、对比、报告、定制模块
```

平台价值不在“内置多少个算法”，而在：

1. 底层计算可信；
2. 算法定义可复现；
3. 用户可以安全地修改参数并构建新算法；
4. 新客户需求优先通过“组合已有积木”实现；
5. 无法组合的需求再沉淀为新的平台积木；
6. 一次定制开发可以转化为长期可复用能力。

---

## 2. 产品边界

### 2.1 平台负责

- EEG/BDF/EDF 等文件导入与标准化
- 多通道数据管理
- 参考方式
- 数字滤波
- 采样率与时间轴
- 窗口切分
- FFT / Welch PSD
- 时频分析
- 频段积分
- 数据质量门
- 伪迹窗口标记
- 标准统计运算
- 用户参数化算法
- 算法版本管理
- 配置指纹
- 算法验证
- 结果可视化
- 算法运行历史
- 科研项目和实验管理
- 定制算法模块接入
- 结果导出和报告

### 2.2 平台不默认承担

- 未验证算法的医学诊断
- 将科研指标自动解释为疾病结论
- 将工程测试通过等同于临床有效性
- 不经统计验证直接声明正常/异常
- 允许任意用户无隔离地执行任意代码
- 用前端显示逻辑替代后端可信计算

---

## 3. 总体架构

完整平台分为七层。

```text
┌──────────────────────────────────────┐
│ 7. 应用层：科研工作台 / 报告 / 项目 / 定制模块 │
├──────────────────────────────────────┤
│ 6. 算法编排层：用户自定义算法 / 参数 / 公式 / DAG │
├──────────────────────────────────────┤
│ 5. 科研积木层：Band Power / Ratio / Log / CV ... │
├──────────────────────────────────────┤
│ 4. 算法模块层：IAPF / FAA / 疲劳 / 客户算法等   │
├──────────────────────────────────────┤
│ 3. 可信计算底座：滤波 / PSD / 时频 / 质量门     │
├──────────────────────────────────────┤
│ 2. 数据标准层：通道 / 参考 / 单位 / 时间 / 元数据 │
├──────────────────────────────────────┤
│ 1. 数据接入层：BDF / EDF / CSV / 实时设备       │
└──────────────────────────────────────┘
```

---

## 4. 第一核心：可信计算底座

这一层是整个平台最重要的基础设施。

原则：

> 用户可以修改参数，但不能绕过“计算契约、单位、版本、质量门、输入输出规范”。

底座应提供稳定、可测试、可版本化的基础能力。

### 4.1 数据读取

支持：

- BDF
- EDF / EDF+
- CSV / TXT（映射导入）
- 未来：BrainVision
- 未来：实时采集流
- 未来：其他生理信号（ECG、EOG、EMG）

统一转换成内部标准结构：

```text
Recording
├─ recording_id
├─ source_format
├─ sample_rate
├─ duration
├─ channels[]
├─ units
├─ annotations
├─ device_metadata
└─ acquisition_metadata
```

### 4.2 通道标准化

必须统一：

- 通道名称
- 别名映射
- 10-20 / 10-10 命名
- 缺失通道
- 辅助通道
- 耳电极/乳突
- EOG/ECG 等非 EEG 通道

例如：

```text
T3 → T7（可选标准映射）
T4 → T8
T5 → P7
T6 → P8
```

注意：原始标签永远保留，不应不可逆替换。

---

## 5. 参考系统

参考方式必须成为独立、显式、可版本化的计算步骤。

支持：

- original recording reference
- average reference
- linked mastoids
- single mastoid
- bipolar montage
- custom reference set
- 自定义通道表达式

每次结果必须记录：

```text
reference.mode
reference.channels
reference.exclude_channels
reference.version
```

例如平均参考：

```text
Fp1-AVG
```

应能够展示其实际数学表达式。

参考变换不能只存在于 UI 状态，必须进入算法契约和结果元数据。

---

## 6. 滤波系统

支持：

- high-pass
- low-pass
- band-pass
- band-stop
- notch
- FIR
- IIR
- Butterworth
- zero-phase
- causal realtime filtering

每个滤波步骤记录：

```text
filter_type
design
order
low_cut
high_cut
notch_freq
Q
phase_mode
implementation
library_version
```

必须明确区分：

- 实时 causal filter
- 离线 zero-phase filter

二者不能使用同一个算法版本名伪装成相同结果。

---

## 7. 窗口系统

窗口是所有后续算法的基础概念。

支持：

- 固定窗口
- 滑动窗口
- overlapping window
- epoch
- event locked window
- baseline window
- dynamic window

统一定义：

```text
window_length_sec
step_sec
overlap_ratio
time_axis_semantics
window_alignment
partial_window_policy
```

例如：

```text
4 s Hann
step = 1 s
center timestamp
```

平台必须永远能够回答：

> 这个结果究竟来自哪一段原始数据？

---

## 8. PSD / 频谱

PSD 是最核心的标准积木之一。

支持：

- FFT spectrum
- Welch PSD
- periodogram
- future: multitaper

标准参数：

```text
method
window_type
window_length
overlap
nfft
detrend
frequency_range
scaling
unit
```

标准输出：

```text
frequency_hz[]
psd_uv2_per_hz[]
```

频段功率必须明确：

```text
band_power = ∫ PSD(f) df
```

并记录积分算法：

- trapezoid
- Simpson（未来）
- bin sum（如有）

---

## 9. 时频分析

标准输出结构：

```text
times_sec[]
frequencies_hz[]
power_matrix[time][frequency]
quality_mask[]
```

必须明确：

```text
matrix_orientation = time × frequency
time_semantics = window_center / window_start
linear_unit = uV²/Hz
display_transform = optional dB
```

前端原则：

> 只展示后端时频结果，不偷偷重新 FFT、重新 PSD 或重新 dB。

---

## 10. 质量门系统

质量门不能与算法核心逻辑混在一起。

质量门应单独成为标准模块。

支持：

- amplitude threshold
- flatline
- clipping
- missing samples
- excessive variance
- sudden jump
- line noise
- custom artifact rule

输出：

```text
quality = clean / rejected / warning
reason_codes[]
metrics{}
```

低质量结果规则：

```text
value = null
status = gate_failed
```

不允许：

```text
value = 0
```

因为 0 是一个真实数值，不能代表“未计算”。

---

## 11. 科研计算积木库

积木是平台可扩展性的核心。

### 11.1 数据选择积木

- Channel Select
- Channel Group
- Region Select
- Time Range
- Epoch Select
- Event Select

### 11.2 信号处理积木

- Re-reference
- Band-pass
- High-pass
- Low-pass
- Notch
- Resample
- Detrend
- Baseline Correction

### 11.3 频谱积木

- PSD
- Band Power
- Relative Band Power
- Peak Frequency
- Peak Power
- Spectral Centroid
- Spectral Slope
- 1/f Fit

### 11.4 时频积木

- Spectrogram
- Band Time Course
- Window Power
- Time-frequency ROI

### 11.5 数学运算积木

- Add
- Subtract
- Multiply
- Divide
- Ratio
- Log
- ln
- Power
- Square Root
- Absolute
- Negate

### 11.6 统计积木

- Mean
- Median
- Min
- Max
- Standard Deviation
- Variance
- CV
- Percentile
- Z-score
- Robust Z-score
- Moving Average
- Exponential Smoothing

### 11.7 跨通道积木

- Channel Average
- Region Average
- Difference
- Laterality Difference
- Ratio
- Weighted Sum

### 11.8 逻辑积木

- Greater Than
- Less Than
- Between
- Threshold
- AND / OR
- Conditional Output

### 11.9 质量控制积木

- Require Clean Epoch
- Minimum Clean Percentage
- Minimum Valid Windows
- Missing Channel Gate
- Minimum Duration Gate

---

## 12. 用户自定义算法系统

### 12.1 第一原则

用户不应该直接修改平台代码。

用户创建的是：

> 算法定义（Algorithm Definition）

而不是：

> 任意 Python 文件。

算法定义是一个可版本化的计算图 DAG。

---

## 13. Algorithm Definition

每个算法必须具备：

```text
algorithm_id
name
description
owner
visibility
version
created_at
updated_at

inputs
parameters
nodes
outputs
quality_rules
units
metadata
validation
```

---

## 14. 示例：用户自定义 Brainbeat

```text
Fz
 ↓
PSD
 ↓
Theta 4–8 Hz
 ↓
Relative Power
 ┐
 ├── Divide → Brainbeat
 ┘
Pz
 ↓
PSD
 ↓
Alpha 8–13 Hz
 ↓
Relative Power
```

平台保存的不是最终结果，而是完整计算图。

---

## 15. 示例：FAA

```text
F4
 ↓
Alpha Power
 ↓
ln
 ┐
 ├─ Subtract → FAA
 ┘
F3
 ↓
Alpha Power
 ↓
ln
```

FAA 本身可以作为：

1. 官方算法模块；
2. 用户自己组合出来的算法；
3. 客户定制算法模板。

---

## 16. 算法节点类型系统

为了防止用户组合出逻辑错误，积木必须有类型。

示例：

```text
EEGSignal
PSDSeries
BandPower
RelativePower
Scalar
TimeSeries
ChannelMap
Boolean
QualityMask
```

节点输入输出必须类型匹配。

例如：

```text
ln()
```

允许：

```text
Scalar
BandPower
```

不直接允许：

```text
RawEEGSignal
```

除非显式定义转换。

---

## 17. 单位系统

单位必须成为一等公民。

支持：

```text
µV
µV²
µV²/Hz
Hz
seconds
ratio
percent
dimensionless
dB re 1 µV²/Hz
```

算法编辑器必须阻止明显错误：

例如：

```text
Alpha Power (µV²) + Peak Frequency (Hz)
```

默认不允许。

---

## 18. 参数系统

参数支持：

- integer
- float
- boolean
- enum
- range
- channel
- channel set
- frequency range
- time range
- formula
- threshold

参数可以定义约束：

```text
min
max
step
default
required
description
unit
```

例如：

```text
AlphaBand:
type = frequency_range
default = [8, 13]
min = 0.5
max = 70
```

---

## 19. 配置指纹

任何算法执行必须生成配置指纹。

指纹至少涵盖：

- 算法版本
- 参数
- 参考方式
- 滤波
- PSD 方法
- 窗口
- 采样率策略
- 通道映射
- 质量门
- 节点图结构

结果必须可以回答：

> 这个数到底是怎么来的？

---

## 20. 可复现性

完整复现一个结果至少需要：

```text
source data checksum
algorithm ID
algorithm version
config hash
library/runtime version
reference
filter chain
window config
channel mapping
quality rules
execution timestamp
```

如果算法修改：

```text
v1.0.0 → v1.1.0
```

旧结果仍能追溯到旧版本。

---

## 21. 算法版本管理

建议采用 Semantic Versioning 思路：

```text
MAJOR.MINOR.PATCH
```

示例：

```text
1.0.0
1.1.0
1.1.1
2.0.0
```

定义：

- PATCH：修复实现，不改变科学定义
- MINOR：增加参数/输出，兼容旧定义
- MAJOR：改变算法定义、公式、关键口径

---

## 22. 算法生命周期

算法状态：

```text
draft
testing
validated_engineering
research_use
deprecated
archived
```

如果未来进入临床研究，可增加：

```text
clinical_research
clinical_validated
```

但不得因为代码测试通过直接进入 clinical_validated。

---

## 23. 算法验证中心

这是平台的重要竞争力。

每个算法都应该能够进入“验证模式”。

### 23.1 标准信号验证

人工构造：

```text
10 Hz sine
20 Hz sine
10 + 20 Hz mixture
known amplitude
known noise
```

验证：

- PSD 峰位置
- 功率
- 频段积分
- 时频位置
- 滤波衰减

### 23.2 黄金数据验证

保存一组：

```text
input
expected output
tolerance
algorithm version
```

### 23.3 交叉实现验证

例如：

```text
Platform PSD
vs
Independent SciPy implementation
```

### 23.4 逐点验证

例如：

```text
117 PSD points
max absolute error
max relative error
PASS/FAIL
```

### 23.5 时频验证

例如：

```text
spectrogram row @ 12 s
vs
static PSD @ 10–14 s
```

---

## 24. 算法验证报告

每次验证生成：

```text
Algorithm
Version
Dataset
Config Hash
Test Cases
Expected
Actual
Tolerance
Pass Rate
Timestamp
Environment
```

验证报告可以导出。

---

## 25. 算法模块系统

平台自带算法与客户算法都使用统一模块协议。

类型：

- Official
- Research Template
- Organization Private
- User Private
- Customer Custom
- Deprecated Legacy

模块不能绕过底座契约。

---

## 26. 官方算法库

平台可以内置：

- PSD
- Spectrogram
- Band Power
- Relative Band Power
- IAPF
- FAA
- Theta/Beta
- Brainbeat-like ratios
- Rhythm Stability
- Spectral Entropy
- 1/f slope
- regional asymmetry
- connectivity（后期）

但每个算法都必须：

- 可查看定义
- 可查看版本
- 可查看参数
- 可查看引用
- 可查看验证状态

---

## 27. 用户算法编辑器

完整形态建议分两种视图。

### 27.1 表单模式

适合大多数科研人员。

例如：

```text
输入 A:
通道 Fz
指标 Theta Relative Power
频段 4–8 Hz

输入 B:
通道 Pz
指标 Alpha Relative Power
频段 8–13 Hz

运算:
A / B

输出:
Custom Brainbeat
```

### 27.2 图形编排模式

高级模式：

```text
Fz → PSD → Theta → RBP ─┐
                        ├→ Divide → Output
Pz → PSD → Alpha → RBP ─┘
```

支持拖拽、连接、参数编辑。

---

## 28. 用户自定义公式

支持安全公式表达式：

```text
(A + B) / C
ln(A) - ln(B)
(A * 0.3) + B
```

禁止直接 eval 任意代码。

使用受限表达式解析器。

---

## 29. 高级代码插件

完整平台长期可以支持 Python 插件，但必须作为高级能力。

不直接允许：

```text
upload arbitrary .py and run on server
```

必须：

- 沙箱
- 资源限制
- CPU quota
- memory quota
- timeout
- 禁止默认联网
- 文件访问隔离
- 依赖白名单
- 环境版本锁定
- 审计日志

高级代码插件不是平台第一核心能力，但保留扩展接口。

---

## 30. 客户定制开发模式

客户需求处理流程：

```text
客户提出指标
↓
判断是否能由现有积木组合
↓
能 → 配置算法模块
不能 → 开发新标准积木
↓
验证
↓
打包成客户私有模块
↓
发布到客户组织空间
```

目标：

> 尽量不修改平台核心代码。

---

## 31. 组织级算法

支持：

```text
个人算法
实验室算法
组织算法
平台官方算法
```

组织管理员可以：

- 发布算法
- 锁定版本
- 禁止成员修改
- 指定默认参数
- 查看验证记录

---

## 32. 算法分享

支持分享：

- Algorithm Definition
- 参数模板
- 验证报告
- 示例数据
- 论文引用
- 说明文档

分享时不必分享原始敏感 EEG 数据。

---

## 33. 实验项目系统

科研用户不只是“跑算法”，还需要项目结构。

```text
Project
├─ Subjects
├─ Sessions
├─ Recordings
├─ Conditions
├─ Algorithms
├─ Runs
├─ Results
└─ Reports
```

---

## 34. Condition / Group

支持：

```text
Eyes Open
Eyes Closed
Baseline
Task A
Task B
Pre
Post
Control
Treatment
```

方便组间比较。

---

## 35. Batch Analysis

支持：

```text
1 个算法
×
100 个受试者
×
多个 session
```

批量运行。

输出：

- success
- failed
- gate_failed
- missing_channel
- insufficient_duration

---

## 36. 结果数据模型

每个结果必须包含：

```text
result_id
recording_id
algorithm_id
algorithm_version
config_hash
channel
time_range
value
unit
quality
status
created_at
provenance
```

---

## 37. Provenance 数据血缘

完整记录：

```text
Raw Recording
→ Reference
→ Filter
→ Window
→ PSD
→ Band Power
→ Ratio
→ Final Score
```

用户可以点一个结果查看完整来源链。

---

## 38. 可视化系统

可视化与算法计算分离。

### 38.1 波形图

支持：

- 临床式固定页
- sweep / overwrite 播放
- 暂停阅图
- 多通道
- montage
- filter display settings
- annotations

### 38.2 PSD

支持：

- 单通道 PSD
- 多通道 overlay
- band shading
- peak marker
- linear / dB

### 38.3 时频图

支持：

- frequency range selector
- band preset
- custom frequency range
- hover values
- band boundary overlays
- window center semantics

### 38.4 频段趋势

例如：

```text
Alpha Power vs Time
Theta/Beta vs Time
```

---

## 39. 算法结果可视化协议

每个算法输出可声明推荐图表：

```text
scalar → metric card
timeseries → line
frequency_series → PSD
time_frequency → heatmap
channel_map → topomap
table → data table
```

这样客户新算法接入后，可以自动生成基础 UI。

---

## 40. 前端不做科学计算

原则：

> 科学计算统一后端完成，前端只做交互、筛选、格式化和可视化。

避免：

- 前后端算法漂移
- 单位不一致
- 版本不可追溯
- 同一算法显示不同结果

---

## 41. API 设计

建议统一：

```text
/api/recordings
/api/preprocessing
/api/psd
/api/spectrogram
/api/algorithms
/api/algorithm-definitions
/api/runs
/api/results
/api/validation
/api/projects
```

---

## 42. 计算任务系统

所有耗时分析走 Job 模型：

```text
queued
running
completed
failed
cancelled
```

支持：

- progress
- cancellation
- retry
- resource tracking

---

## 43. 缓存系统

相同输入：

```text
data checksum
+ algorithm version
+ config hash
```

应允许复用结果。

避免重复计算。

---

## 44. 数据不可变原则

原始 EEG 文件：

> 永远只读。

所有处理结果都作为派生数据保存。

不能覆盖原始数据。

---

## 45. 导出系统

支持：

- CSV
- JSON
- Parquet（后期）
- 图像
- PDF 报告
- Algorithm Definition
- Validation Report

---

## 46. 科研报告

报告必须区分：

```text
Measured Data
Algorithm Output
Research Interpretation
Clinical Conclusion
```

系统默认只输出前两类。

---

## 47. 权限系统

角色：

- Viewer
- Researcher
- Algorithm Author
- Organization Admin
- Platform Admin

控制：

- 数据
- 算法
- 项目
- 导出
- 发布
- 验证

---

## 48. 审计日志

记录：

```text
who
when
what
algorithm
version
parameters
dataset
result
```

科研可追溯非常重要。

---

## 49. 数据安全

至少包括：

- 项目隔离
- 组织隔离
- 加密存储
- 传输加密
- 权限控制
- 操作日志
- 数据删除策略
- 脱敏流程

---

## 50. 计算环境可复现

记录：

```text
Python version
SciPy version
NumPy version
backend build
algorithm package version
```

必要时：

- Docker image digest
- dependency lock

---

## 51. 未来插件系统

插件类型：

```text
Data Import Plugin
Preprocessing Plugin
Metric Plugin
Algorithm Plugin
Visualization Plugin
Export Plugin
```

所有插件遵循统一 manifest。

---

## 52. Plugin Manifest

示例：

```text
plugin_id
name
version
type
inputs
outputs
parameters
permissions
dependencies
runtime
validation
```

---

## 53. 算法市场 / 模块中心（长期）

不是第一阶段重点，但架构预留。

可分：

- Official
- Verified Partner
- Organization Private
- Community

每个模块展示：

- 作者
- 版本
- 定义
- 验证状态
- 引用
- 支持数据
- 兼容平台版本

---

## 54. 临床模块的隔离

如果未来进入临床：

必须独立建立：

- 临床数据集
- 专家标注
- 统计验证
- 医疗人员验收
- 法规评估
- 风险管理

科研算法不能因为“工程验证通过”自动变成临床算法。

---

## 55. 测试体系

### 55.1 单元测试

验证：

- window
- filter
- PSD
- band integration
- reference
- math nodes

### 55.2 黄金数据测试

固定输入固定输出。

### 55.3 Property-based Test

例如：

```text
RBP sum ≈ 1
PSD >= 0
same config → same output
```

### 55.4 Cross Validation

与：

- SciPy
- MNE
- MATLAB（如有）
- 人工解析信号

交叉验证。

### 55.5 End-to-End

从：

```text
BDF → final algorithm result
```

全链验证。

---

## 56. 完整路线图

### Phase A：可信底座收拢

目标：

- BDF/EDF
- reference
- filter
- waveform
- PSD
- spectrogram
- quality gate
- units
- version
- config hash
- validation

完成标准：

> 基础计算可以独立复算，并有验证报告。

---

### Phase B：科研积木标准化

目标：

将现有“写死算法”拆成标准积木。

完成：

- Band Power
- Relative Power
- Ratio
- ln
- Add/Subtract
- Mean
- Median
- CV
- Channel Average
- Region Average
- Peak Frequency

---

### Phase C：算法定义引擎

完成：

- Algorithm Definition schema
- DAG execution
- typed nodes
- unit checking
- parameter system
- version system
- config fingerprint
- validation hooks

---

### Phase D：用户算法编辑器

完成：

- form builder
- visual DAG builder
- preview
- validation
- save
- clone
- version
- compare

---

### Phase E：项目与批处理

完成：

- Project
- Subject
- Session
- Condition
- Batch Run
- Comparison
- Result Table

---

### Phase F：客户定制模块

完成：

- private algorithms
- private plugins
- organization publishing
- role permissions
- delivery package
- customer-specific dashboards

---

### Phase G：高级插件与代码扩展

完成：

- sandboxed Python plugins
- dependency policy
- resource isolation
- signed packages
- plugin validation

---

### Phase H：生态能力

完成：

- algorithm registry
- template library
- partner modules
- sharing
- marketplace（如商业模式需要）

---

## 57. 优先级原则

开发任何新功能前问三个问题：

### 1. 这是底座能力，还是某个算法特例？

如果是特例，优先抽象。

### 2. 这个能力未来能否被其他算法复用？

如果能，应该做成积木。

### 3. 用户改参数后是否还能复现？

如果不能，不应发布。

---

## 58. 技术债控制

避免：

```text
if algorithm == "FAA":
...
elif algorithm == "IAPF":
...
elif algorithm == "customer_x":
...
```

目标：

```text
Algorithm Definition
→ generic executor
```

特殊算法只在真正无法被积木表达时成为插件。

---

## 59. 完整验收标准

平台最终应具备：

- [ ] 原始数据不可变
- [ ] 所有科学计算后端统一
- [ ] 所有结果可追溯
- [ ] 所有算法有版本
- [ ] 所有算法有配置指纹
- [ ] 所有输出有单位
- [ ] 所有时间窗口可追溯
- [ ] 所有质量门有原因
- [ ] 用户可以修改参数
- [ ] 用户可以组合算法
- [ ] 用户可以保存算法
- [ ] 用户可以复制算法
- [ ] 用户可以比较算法版本
- [ ] 用户可以验证算法
- [ ] 平台可以生成验证报告
- [ ] 官方算法与用户算法使用统一契约
- [ ] 客户算法可以私有发布
- [ ] 新算法尽量不修改平台核心
- [ ] 支持批量科研分析
- [ ] 支持科研项目和条件分组
- [ ] 支持完整数据血缘
- [ ] 前端不重新计算科学结果
- [ ] 工程验证与临床验证明确分离

---

## 60. 最终产品形态

完整版本最终不应该让用户觉得：

> “这是一个内置了几十个 EEG 指标的软件。”

而应该让用户觉得：

> “这是一个脑科研计算工作台。”

科研人员可以：

```text
导入 EEG
→ 选择数据
→ 设置预处理
→ 查看 PSD/时频
→ 选择科研积木
→ 修改频段
→ 修改窗口
→ 组合公式
→ 创建算法
→ 保存版本
→ 批量运行
→ 验证结果
→ 对比实验
→ 导出报告
```

定制客户可以：

```text
提出算法需求
→ 使用已有积木快速配置
→ 缺少能力时增加一个标准积木
→ 封装为客户私有算法模块
→ 独立版本
→ 独立验证
→ 独立发布
```

平台团队则从：

> “不断开发新的写死页面和算法”

转变为：

> “维护可信底座、扩充科研积木、交付算法模块”。

---

## 61. 核心战略结论

这个平台最重要的资产不是某一个 EEG 指标。

真正的资产是：

1. **可信计算底座**
2. **标准科研积木库**
3. **算法定义协议**
4. **可复现版本系统**
5. **算法验证体系**
6. **插件与定制交付能力**

当这六件事成熟后：

- PSD 是一个积木；
- FAA 是一个算法定义；
- IAPF 是一个算法模块；
- 客户疲劳指数是一个私有模块；
- 新科研思想可以快速成为新的算法；
- 平台核心不需要因为每个客户而不断重写。

---

## 62. 一句话产品定义

> **一个让科研人员能够“看数据、改参数、组算法、做验证、跑实验、复现结果”的可扩展脑科研计算平台。**
