# EEG Topomap Specification

## Purpose

为已有 EEG 通道结果提供统一、可追溯的头皮空间展示能力，使不同算法能够复用同一套时间、通道位置、单位、质量和空间资格规则，而不重复实现地形图逻辑。

## Requirements

### Requirement: Topomap input is an explicit windowed channel result
Topomap input MUST be one selected scalar spatial field from an explicit file-absolute-time algorithm result. It MUST include `recording_id`, result/window identity, `window_start_s`, `window_end_s`, original channel order, one value and quality state per channel, a stable field identity, an explicit unit, algorithm version, and the bound montage snapshot identity. A frequency-resolved source MUST additionally identify the selected actual frequency bin and its `frequency_hz`. Internal values MUST follow the platform scientific-unit contract; public fields MUST declare their unit. The topomap MUST NOT infer time, channel positions, field identity, or values from the display clock or frontend.

#### Scenario: Algorithm supplies a valid band result
- **WHEN** an algorithm provides one complete window of per-channel values for a band or other scalar metric
- **THEN** its adapter preserves the window identity, original channel order, field identity, unit, algorithm version, montage identity, and per-channel quality
- **AND** the spatial projection consumes only that field's values

#### Scenario: PSD supplies one actual frequency bin
- **WHEN** PSD supplies its ordered channel-by-frequency result and a consumer selects one returned frequency bin
- **THEN** the adapter selects that exact bin for every channel and reports its exact `frequency_hz` with unit `V^2/Hz`
- **AND** it does not integrate, average, interpolate, or substitute values across frequency bins

#### Scenario: Result has no usable numeric value
- **WHEN** a channel value is missing, non-finite, quality-invalid, or its result window is incomplete
- **THEN** that channel is marked unavailable and excluded from spatial interpolation
- **AND** the system does not substitute zero, a previous window, a neighboring channel, or a value from another frequency bin

### Requirement: Spatial eligibility comes from authoritative channel metadata
地形图 MUST 复用现有通道配置中的标准电极位置解析结果，不得维护第二套坐标表、别名表或根据通道文本猜测位置。只有具备有效标准或明确的图示位置、且与结果通道身份一致的 EEG 通道才可进入空间投影。参考、地线、未解析通道和没有明确定义空间含义的双极导联 MUST 标记为空间不可用。

#### Scenario: Standard electrode positions are available
- **WHEN** 结果通道与已保存的标准电极位置快照一一对应
- **THEN** 地形图使用该快照的二维头顶坐标，并保留位置来源、坐标框架和来源版本

#### Scenario: Bipolar result has no spatial rule
- **WHEN** 一个双极导联结果没有声明如何映射到头皮空间
- **THEN** 该结果不得被分配到任一端点，地形图返回空间不可用原因

### Requirement: Electrode positions are versioned analysis inputs
通道位置编辑 MUST 区分未保存草稿和已应用的 montage 版本。保存/应用 MUST 生成包含通道顺序、MapX/MapY、位置来源、REF/GND 标识和版本身份的不可变快照。统一录音访问层 MAY 为缺少二维字段的历史快照物化标准模板位置，但必须把解析来源和版本写入同一 provenance；地形图不得自行解析。分析运行、地形图结果和缓存身份 MUST 绑定明确的 montage 版本；系统 MUST NOT 通过修改算法版本号代替位置版本。

#### Scenario: User edits a position without applying it
- **WHEN** 用户在通道配置中拖动电极但尚未保存/应用
- **THEN** 只更新配置草稿，当前算法运行、缓存和历史地形图不得改变

#### Scenario: User applies a new position
- **WHEN** 用户保存/应用新的电极位置
- **THEN** 系统生成新的 montage 版本；后续新建的算法运行使用该版本，旧运行和旧结果继续使用原绑定版本

#### Scenario: Position changes while a run is active
- **WHEN** 当前算法运行已经绑定旧 montage，而用户应用了新位置
- **THEN** 旧运行保持旧快照；新的位置只能通过新的运行代次重新计算，旧窗口结果不得被新位置重绘

### Requirement: Channel configuration and topomap share projection geometry
通道配置图和地形图 MUST 使用同一归一化 MapX/MapY 坐标契约、头部中心、半径、方向、鼻子和耳朵几何。共享坐标输入在两个界面中的相对像素位置 MUST 一致；任何界面不得单独定义头部缩放常量或重新转换坐标方向。

#### Scenario: Same electrode is shown in both views
- **WHEN** 同一 montage 版本中的同一电极同时显示在通道配置图和 RBP 地形图
- **THEN** 两个视图使用同一投影结果，电极相对头部中心的位置一致；画布大小不同只能造成统一比例缩放

#### Scenario: Display and interpolation have different channel sets
- **WHEN** 通道配置显示全部启用输入，而地形图过滤 REF/GND、未解析、无效值或无空间资格通道
- **THEN** 两者仍使用同一位置和几何契约；地形图记录每个被过滤通道的结构化原因，不把过滤误认为坐标不一致

### Requirement: Projection and interpolation are deterministic and bounded
地形图 MUST 声明空间投影/插值方法、头部边界、有效点数量和无数据区域规则。插值 MUST 只使用空间可用且质量有效的通道；有效点不足、坐标重复或插值失败时，系统 MUST 返回结构化不可用状态，不得生成看似完整的伪造热图。

#### Scenario: Sufficient valid channel values
- **WHEN** 当前窗口有足够的空间有效通道值且坐标无冲突
- **THEN** 系统按声明的方法生成有界空间网格，并将方法、参数和参与通道记录到结果证据；插值网格覆盖共享头部轮廓，电极位置仍可保留标准模板的内边界，网格分辨率不得低于 65×65

#### Scenario: Insufficient or contradictory spatial inputs
- **WHEN** 有效通道数量不足、坐标重复或插值超出头部边界
- **THEN** 地形图显示结构化不可用原因和参与通道信息，不静默扩大边界或填充缺失区域

### Requirement: Topomap follows the workspace time coordinator
数据回放中的地形图 MUST 使用工作区统一时间协调器发布的当前绝对时间窗口；算法结果尚未到达、游标跳转、暂停、重置或窗口失效时，地形图状态 MUST 与该窗口身份一致。旧请求返回时不得覆盖较新的窗口结果。

#### Scenario: Cursor advances to a new window
- **WHEN** 回放工作区发布新的绝对时间窗口且算法结果已就绪
- **THEN** 地形图只展示该窗口的结果，并显示与结果一致的开始和结束时间

#### Scenario: Result arrives after cursor moved
- **WHEN** 旧窗口的异步结果在新窗口已经发布后才返回
- **THEN** 地形图丢弃旧窗口更新，不回退显示旧空间分布

#### Scenario: A superseded topomap request returns no result
- **WHEN** 时间协调器以空结果表示地形图请求已被新窗口取代
- **THEN** 回放视图忽略该空结果，不把它当作显式清空；只有显式解除分析或状态重置才清除地形图

### Requirement: Algorithms register data adapters instead of drawing maps
Algorithms that support topomaps MUST register an adapter that identifies the source result, stable scalar field identifiers, units, window identity, channel order, quality, reference semantics, and spatial eligibility. An adapter MUST project values already present in the authoritative algorithm result; it MUST NOT implement WPF drawing, duplicate coordinate resolution, reread raw recording samples, or recompute the source algorithm. One algorithm MAY expose multiple fields, but every field MUST identify its value meaning and version.

#### Scenario: RBP registers band outputs
- **WHEN** RBP exposes per-channel relative or absolute power for Delta, Theta, Alpha, Beta, or Gamma
- **THEN** its adapter identifies each requested power/band field and reads only the matching structured RBP result array
- **AND** it does not derive absolute power from relative shares or diagnostic evidence

#### Scenario: PSD registers a frequency-bin field
- **WHEN** PSD exposes its current ordered frequency axis and channel-resolved result
- **THEN** its adapter registers the selected bin as one spatial field with the actual bin frequency and `V^2/Hz` unit
- **AND** it shares the same interpolation and position path used by RBP

### Requirement: Topomap output is traceable and explicit about template positions
地形图结果 MUST 记录来源算法、算法版本、参数快照、录音身份、绝对时间窗口、参与和排除的通道、坐标来源、空间方法、单位和质量/不可用原因。界面 MUST 使用实心黑点表示参与计算的电极；详细时间、单位和标准模板来源保留在结果契约与可追溯证据中，紧凑图表视图可以不重复显示这些文字。

#### Scenario: User inspects a rendered map
- **WHEN** 地形图成功展示一个指标
- **THEN** 用户可以看到指标名称、单位、时间窗口和“标准模板位置”提示，并能追溯到算法结果和参与通道

#### Scenario: Backend cannot produce a map
- **WHEN** 结果不存在、算法失败或空间投影不可用
- **THEN** API 返回稳定错误码或结构化不可用状态及 request/session 关联信息，前端显示原因而不是空白或伪造颜色

### Requirement: Topomap visuals are prepared outside the UI dispatcher
WPF topomap visuals MUST prepare grid bitmaps, color ranges, and contour geometry outside the UI dispatcher and freeze every WPF drawing resource published across threads. `OnRender` MUST NOT traverse grid values to generate pixels or calculate contours; it SHALL draw prepared resources, head/electrode geometry, and lightweight scale labels. When the selected field, actual frequency bin, or canvas size changes, the system MUST cancel obsolete preparation and publish only a result that still matches the active field, window, size, and generation.

#### Scenario: A topomap window updates during playback
- **WHEN** the current window, selected field/frequency bin, or canvas size changes
- **THEN** the bitmap and contours are prepared in the background and frozen before UI publication
- **AND** a late result from an older field, window, or generation is discarded

#### Scenario: A new visual preparation is superseded
- **WHEN** a new window or selected field arrives before old visual preparation completes
- **THEN** obsolete preparation is cancelled and cannot replace the current field
- **AND** drawing resources cross to the UI thread only after they are frozen

### Requirement: Playback updates preserve topomap visual instances
Topomap presentation items MUST retain stable identity for the active Review session. When a new result window arrives, the client SHALL update the selected field data on the existing visual item and MUST NOT recreate the whole control collection. Adding or removing a requested field MAY add or remove only the corresponding field item.

#### Scenario: A new topomap response arrives during playback
- **WHEN** the same analysis session receives the next result window for its active field selection
- **THEN** the existing topomap control instances are updated with the new window's field data
- **AND** the prior complete visual remains visible until the next frozen visual is ready

### Requirement: Select RBP topomap power representation
RBP topomap fields SHALL identify both power representation and band, using the current structured relative or absolute result arrays. Relative fields SHALL declare unit `ratio`; absolute fields SHALL declare unit `uV^2`. Both SHALL use the same RBP window, recording, result identity, montage snapshot, channel order, spatial eligibility, and interpolation contract. The client MUST request explicit field identifiers and MUST NOT derive absolute power from relative shares or diagnostic evidence. An absolute field absent from a stored result SHALL be unavailable; it MUST NOT be reconstructed from another source.

#### Scenario: User selects absolute power
- **WHEN** the user selects an absolute-power field and the current result contains its structured absolute array
- **THEN** the response uses that exact array and declares unit `uV^2`
- **AND** it preserves the same window and montage identity as the relative field

#### Scenario: User selects relative power
- **WHEN** the user selects a relative-power field
- **THEN** the response uses the matching structured ratio array and declares unit `ratio`
- **AND** it uses the same result window and montage snapshot as an absolute field

#### Scenario: Absolute output is absent from a stored result
- **WHEN** absolute power is requested for a result without the structured array
- **THEN** that field is returned as unavailable with a stable reason
- **AND** no diagnostic evidence, other window, or other montage is used as a substitute

### Requirement: Emit request-correlated topomap timing diagnostics
For a successful topomap request, backend diagnostics SHALL report the request ID, result window ID, recording ID, power representation, requested band filter, backend service duration, per-band interpolation duration, and response serialization duration. Diagnostics SHALL NOT include raw samples or result value arrays. Timings SHALL remain operational metadata and SHALL NOT change scientific results, cache identity, or algorithm version.

#### Scenario: Diagnose a slow single-band topomap response
- **WHEN** one topomap request takes longer than expected
- **THEN** its request-correlated log entries distinguish backend band interpolation from response serialization
- **AND THEN** the response still contains only the requested band and the original result identity

### Requirement: Keep RBP topomap color scales stable during playback
Relative-power maps SHALL use the fixed domain `0–1` with unit `ratio`. Absolute-power maps SHALL establish their display bounds from the first ready window for each RBP run and frequency band, then retain those bounds for the remainder of that run. Power values outside the absolute display bounds SHALL saturate at the corresponding end color; displayed endpoints SHALL remain the locked bounds. These display bounds SHALL NOT alter backend values, scientific units, or quality state.

#### Scenario: Relative power remains comparable between windows
- **WHEN** two relative-power windows for the same or different bands are rendered
- **THEN** both use the fixed `0–1 ratio` color domain

#### Scenario: Absolute power locks bounds for one run and band
- **WHEN** the first ready absolute-power window for an RBP run and band is rendered
- **THEN** its finite grid minimum and maximum establish the color bounds
- **AND THEN** later windows for that run and band retain the same bounds

#### Scenario: A different run receives independent bounds
- **WHEN** a new RBP run is rendered for the same recording and band
- **THEN** its first ready absolute-power window establishes new bounds

#### Scenario: A value exceeds locked absolute bounds
- **WHEN** a later grid value is below or above the locked bounds
- **THEN** its color is clamped to the lower or upper endpoint color
- **AND THEN** the underlying EEG result value remains unchanged

### Requirement: Topomap transport uses explicit scalar field identifiers
The current topomap request MUST name one or more unique `field_ids` bound to a current result identity. The response MUST return those scalar spatial fields under `fields`, with each field's identifier, label, unit, field-specific metadata, per-channel points, quality, grid, and provenance. Frequency fields MUST report the exact selected `frequency_hz`. The current transport MUST NOT use the former `bands`, `power_type`, or `band_id` contract, infer a default field when `field_ids` are absent, or silently map an unknown field to another field. Unsupported identifiers MUST produce a structured invalid-request response; a known field with no usable values MUST produce an explicit unavailable field.

#### Scenario: Request relative or absolute RBP fields
- **WHEN** the client requests explicit relative or absolute RBP field identifiers for one or more bands
- **THEN** the response contains only those requested fields with each source unit and RBP result identity preserved
- **AND** the request does not use `power_type` or `band_id`

#### Scenario: Request a PSD frequency field
- **WHEN** the client requests a PSD field identifier for a bin present in the bound result
- **THEN** the response contains one field for that exact bin, its actual frequency, the same PSD window, and unit `V^2/Hz`
- **AND** the request does not start another PSD run or read raw recording samples

#### Scenario: Request an absent or invalid field
- **WHEN** a requested frequency bin is not present in the bound result or a field identifier is unsupported
- **THEN** the API reports a structured invalid-field error
- **AND** it does not choose the nearest frequency, a default band, or an older result
