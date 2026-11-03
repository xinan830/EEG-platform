# EEG Topomap Specification

## Purpose

为已有 EEG 通道结果提供统一、可追溯的头皮空间展示能力，使不同算法能够复用同一套时间、通道位置、单位、质量和空间资格规则，而不重复实现地形图逻辑。

## Requirements

### Requirement: Topomap input is an explicit windowed channel result
地形图输入 MUST 是一个明确的文件绝对时间窗口结果，并包含 `recording_id`、`window_start_s`、`window_end_s`、通道原始名称顺序、每个通道的数值、公共单位、指标或频段标识、算法版本和结果质量状态。内部数值单位 MUST 遵守平台科学单位契约；公共响应 MUST 显式声明单位。地形图不得从显示坐标或前端计时器推断时间。

#### Scenario: Algorithm supplies a valid band result
- **WHEN** 一个算法为同一绝对时间窗口返回逐通道 Delta、Theta、Alpha、Beta、Gamma 结果
- **THEN** 地形图适配器一次保留该窗口边界、原始通道顺序、每个频段标识、单位、算法版本和每个通道质量状态，并为每个频段生成独立空间展示

#### Scenario: Result has no usable numeric value
- **WHEN** 某通道值为缺失、非有限、无效质量或结果窗口尚未完成
- **THEN** 该通道被标记为不可用并从空间插值输入中排除，系统不得用零值、上一帧或邻近通道值替代

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
支持地形图的算法 MUST 通过统一适配契约声明指标或频段、逐通道值的单位、窗口能力、参考方式和空间资格；算法模块不得包含 WPF 绘图代码或重复坐标解析。一个算法可以注册多个频段或指标，但每个展示项必须有稳定的标识和版本。

#### Scenario: RBP registers band outputs
- **WHEN** RBP 声明 Delta、Theta、Alpha、Beta 或 Gamma 的逐通道相对功率结果
- **THEN** 地形图框架一次消费五个频段并复用同一投影和渲染路径，每个频段独立显示，不为每个频段创建独立绘图实现

### Requirement: Topomap output is traceable and explicit about template positions
地形图结果 MUST 记录来源算法、算法版本、参数快照、录音身份、绝对时间窗口、参与和排除的通道、坐标来源、空间方法、单位和质量/不可用原因。界面 MUST 使用实心黑点表示参与计算的电极；详细时间、单位和标准模板来源保留在结果契约与可追溯证据中，紧凑图表视图可以不重复显示这些文字。

#### Scenario: User inspects a rendered map
- **WHEN** 地形图成功展示一个指标
- **THEN** 用户可以看到指标名称、单位、时间窗口和“标准模板位置”提示，并能追溯到算法结果和参与通道

#### Scenario: Backend cannot produce a map
- **WHEN** 结果不存在、算法失败或空间投影不可用
- **THEN** API 返回稳定错误码或结构化不可用状态及 request/session 关联信息，前端显示原因而不是空白或伪造颜色

### Requirement: Topomap visuals are prepared outside the UI dispatcher
WPF 地形图 MUST 在后台生成网格位图、颜色范围和等值线几何，并冻结所有跨线程发布的 WPF 绘图资源。`OnRender` MUST NOT 遍历网格生成像素或计算等值线；它只绘制已准备的资源、头部/电极和轻量文字标尺。新的频段或画布尺寸到达时，系统 MUST 取消旧准备任务，并且只发布仍匹配当前频段、尺寸和代次的结果。

#### Scenario: A topomap window updates during playback
- **WHEN** 当前频段的网格结果或画布尺寸发生变化
- **THEN** 位图和等值线在后台准备并冻结，UI 线程绘制已完成的快照；迟到的旧代次结果被丢弃，图表顺序和算法数值保持不变

#### Scenario: A new visual preparation is superseded
- **WHEN** 新窗口在旧窗口的位图或等值线准备完成前到达
- **THEN** 旧任务被取消，旧结果不得覆盖新窗口；绘制资源仅在冻结后跨线程交给 UI

### Requirement: Playback updates preserve topomap visual instances
五频段地形图的展示项身份 MUST 在同一回放分析会话内保持稳定。新窗口到达时只更新各展示项绑定的频段数据，不得每次替换整个 ItemsSource 并重建地形图控件；显式清除或改变频段数量时才增删展示项。

#### Scenario: A new topomap response arrives during playback
- **WHEN** 同一分析会话收到下一窗口的五频段结果
- **THEN** 五个地形图控件保持原有实例并更新 Band 输入，后台准备期间继续呈现上一张完整快照，完成后切换到新快照

### Requirement: Select RBP topomap power representation
The RBP topomap SHALL accept an explicit power representation, `relative` or `absolute`, independently from the selected frequency band. The response SHALL identify the selected representation and declare `ratio` for relative maps or `uV^2` for absolute maps. Both representations SHALL use the same RBP result window, recording identity, montage snapshot, channel order, spatial eligibility rules, and interpolation contract. Omitting the representation SHALL select `relative` for existing callers. The client SHALL request the selected representation from the backend and SHALL NOT derive absolute power from relative shares or diagnostic evidence.

#### Scenario: User selects absolute power
- **WHEN** the user selects absolute power and a current RBP result contains structured absolute-power arrays
- **THEN** the selected frequency band is rendered from the corresponding absolute-power array with unit `uV^2`
- **AND THEN** the result uses the same current window and montage snapshot as the relative map

#### Scenario: User selects relative power
- **WHEN** the user selects relative power or an existing API caller omits the representation
- **THEN** the selected frequency band is rendered from the existing ratio array with unit `ratio`

#### Scenario: Absolute output is absent from a stored result
- **WHEN** absolute power is requested for a result that does not declare its structured absolute-power array
- **THEN** the API returns a structured unavailable map with a stable reason
- **AND THEN** it does not read or reconstruct a value from calculation-trace evidence, another window, or another montage version

### Requirement: Limit topomap interpolation to the selected frequency band
The run and dynamic topomap APIs SHALL accept an optional `band_id` from the closed set Delta, Theta, Alpha, Beta, and Gamma. When omitted, the response SHALL contain all five bands. When supplied, the response SHALL contain and interpolate only that band. Band selection SHALL be independent from power representation and SHALL use the same current result window, montage snapshot, channel order, units, and quality rules. It SHALL NOT trigger RBP recomputation. An unsupported band identifier SHALL be rejected as an invalid request.

#### Scenario: User selects one band
- **WHEN** the Review user selects Alpha and the active power representation is absolute
- **THEN** the client requests only Alpha for the current window
- **AND THEN** the backend returns and interpolates one `alpha` band in `uV^2`

#### Scenario: User selects all bands or an existing caller omits band_id
- **WHEN** the frequency-band selection is All or an API caller omits `band_id`
- **THEN** the backend returns the existing five-band response

### Requirement: Emit request-correlated topomap timing diagnostics
For a successful topomap request, backend diagnostics SHALL report the request ID, result window ID, recording ID, power representation, requested band filter, backend service duration, per-band interpolation duration, and response serialization duration. Diagnostics SHALL NOT include raw samples or result value arrays. Timings SHALL remain operational metadata and SHALL NOT change scientific results, cache identity, or algorithm version.

#### Scenario: Diagnose a slow single-band topomap response
- **WHEN** one topomap request takes longer than expected
- **THEN** its request-correlated log entries distinguish backend band interpolation from response serialization
- **AND THEN** the response still contains only the requested band and the original result identity
