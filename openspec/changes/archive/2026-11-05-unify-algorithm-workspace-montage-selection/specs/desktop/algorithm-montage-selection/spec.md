## MODIFIED Requirements

### Requirement: Link algorithm workspace inputs to its public montage instance

Standalone algorithm input channels SHALL be derived from the algorithm workspace's `WorkspaceMontageSelection` snapshot and the algorithm's declared roles. The catalog may retain role/channel subset choices, but SHALL NOT retain an independent montage selection or synchronize a second selector.

#### Scenario: Switch the algorithm workspace montage

- **WHEN** the user changes the algorithm workspace montage from REF to average reference
- **THEN** selected roles remap to corresponding outputs in the new snapshot where available
- **AND** the visible input channels and serialized request use that same snapshot

#### Scenario: A required algorithm role is absent

- **WHEN** the selected algorithm-workspace snapshot cannot resolve a required role
- **THEN** the role is reported unavailable with a structured mapping reason
- **AND** the algorithm does not keep the previous snapshot output or use a positional fallback

#### Scenario: Switch REF to average reference

- **WHEN** 记录的有效通道角色为通道 1、通道 2，用户将导联配置从 REF 切换为 AV
- **THEN** 分析导联选项从 “通道1-REF”、“通道2-REF” 更新为 “通道1-AV”、“通道2-AV”
- **AND** 已选通道 1 自动切换为 “通道1-AV”
- **AND** 页面显示的分析导联与实际请求输入一致

#### Scenario: Switch the review montage while retaining algorithm roles

- **WHEN** 用户在回放工作区将导联从 REF 切换为 AV
- **THEN** 每个算法面板将其已选语义角色映射到 AV 快照中的对应输出
- **AND** 面板显示的输入与提交请求使用相同快照及输出顺序

#### Scenario: Selected role is unavailable in a montage

- **WHEN** 当前导联配置不能为已选通道角色生成对应导联
- **THEN** 该选项不可选
- **AND** 页面显示缺失源通道或不兼容原因
- **AND** 不得静默改用另一个通道或保留旧导联结果

#### Scenario: A selected review role is unavailable

- **WHEN** 工作区所选快照无法提供算法必需角色
- **THEN** 该角色以结构化原因标记不可用
- **AND** 算法不得保留旧快照输入或替换成其他通道

### Requirement: Refresh algorithm work after selection changes

A standalone algorithm montage change SHALL invalidate old preview/full-run state and use the existing supersede/cancellation path before scheduling new work.

#### Scenario: Change algorithm montage while a request is active

- **WHEN** the selection revision changes while a Run or preview is active
- **THEN** the old work is cancelled or rejected by the current revision
- **AND** its result cannot overwrite the new selection's state

#### Scenario: Change montage while no run is active

- **WHEN** 用户将算法详情从 REF 切换到 AV
- **THEN** 当前算法的请求配置立即变为 AV 对应分析导联
- **AND** 运行结果、动态预览和状态清除或标记为旧配置
- **AND** 新计算使用 AV 信号，不复用 REF 结果

#### Scenario: Change the review montage while no algorithm request is active

- **WHEN** 用户在回放工作区将共享导联从 REF 切换到 AV
- **THEN** 所有算法面板的请求配置解析到 AV
- **AND** REF 的输出被清除或标记为过期
- **AND** 新计算使用 AV 输入且不复用 REF 结果

#### Scenario: Change montage while a run or preview is active

- **WHEN** 用户切换导联配置时旧 Run、动态窗口请求或后台全量任务仍在运行
- **THEN** 旧请求被取消或其返回结果被配置修订拒绝
- **AND** 旧进度和结果不得覆盖新配置
- **AND** 新配置可以提交新的全量、按需或组合执行

#### Scenario: Change the review montage while a panel request is active

- **WHEN** 回放工作区导联变化时旧 Run、预览或窗口请求仍在执行
- **THEN** 旧工作被取消或按选择修订拒绝发布
- **AND** 旧结果不能覆盖当前面板，新的请求只使用一次捕获的当前快照

#### Scenario: Switch montage for a paired or multi-channel algorithm

- **WHEN** FAA、Brainbeat 或其他声明多个输入角色的算法切换导联配置
- **THEN** 每个输入角色都映射到同一当前配置下的对应导联
- **AND** 配对关系、通道顺序和算法专属质量规则保持不变
- **AND** 缺少任一角色时 Run 结构化失败，不以单通道替代

#### Scenario: Request a cursor window while full analysis is progressing

- **WHEN** 组合模式的全量任务保留了待发布窗口且用户请求另一个游标窗口
- **THEN** 全量生产任务不占用按需队列、项目或记录的按需额度
- **AND** 按需生产任务仍受相应容量上限和共享计算并发限制
- **AND** 同一窗口已有活动生产者时复用该生产者，不创建重复计算或夺取运行中的租约
